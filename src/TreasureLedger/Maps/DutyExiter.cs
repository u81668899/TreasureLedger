using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TreasureLedger.Maps;

/// <summary>
/// 退出副本。
///
/// 游戏里没有命令能直接退本，只能走 UI 流程：
///   打开「任务情报」→ 点「退出」→ 确认框点「是」
///
/// 这里用 RaptureAtkUnitManager 拿 Addon 指针，再 FireCallback 触发按钮，
/// 比模拟鼠标点击稳，也不依赖窗口位置。
///
/// 注意：
/// - Addon 名字随版本可能变，这里尝试多个候选
/// - 任何一步失败都只记日志，不抛异常
/// </summary>
public sealed unsafe class DutyExiter : IDisposable
{
    private readonly IPluginLog log;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly Chat.GameCommandExecutor executor;

    /// <summary>可能包含「退出」按钮的 Addon。</summary>
    private static readonly string[] InfoAddons =
    {
        "ContentsFinder",
        "ContentsFinderConfirm",
        "ContentsInfo",
        "JournalDetail",
    };

    private const string YesnoAddon = "SelectYesno";

    private bool waitingForYesno;
    private DateTime pendingSince = DateTime.MinValue;

    public DutyExiter(IPluginLog log, IAddonLifecycle addonLifecycle, Chat.GameCommandExecutor executor)
    {
        this.log = log;
        this.addonLifecycle = addonLifecycle;
        this.executor = executor;

        addonLifecycle.RegisterListener(AddonEvent.PostSetup, YesnoAddon, OnYesnoSetup);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, YesnoAddon, OnYesnoSetup);
    }

    /// <summary>最近一次尝试的结果，给 UI 显示。</summary>
    public string LastResult { get; private set; } = string.Empty;

    public bool WaitingForConfirm => waitingForYesno;

    /// <summary>请求退出副本。返回 true 表示成功发起了退出流程。</summary>
    public bool RequestExit()
    {
        try
        {
            // 已经有确认框就直接点「是」
            if (TryClickYesno())
            {
                LastResult = "已确认退出";
                return true;
            }

            // 没开确认框：直接发游戏原生命令打开任务搜索器，
            // 再由玩家/后续逻辑点退出。
            // 注意：/dutyfinder 只是打开窗口，退出仍需点按钮或走确认框。
            if (executor.Execute("/dutyfinder"))
            {
                LastResult = "已打开任务搜索器，等待退出按钮";
                waitingForYesno = true;
                pendingSince = DateTime.Now;
                return true;
            }

            LastResult = "执行 /dutyfinder 失败";
            return false;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 退本异常");
            LastResult = "异常：" + ex.Message;
            return false;
        }
    }

    /// <summary>拿 Addon 指针。</summary>
    private static AtkUnitBase* GetAddon(string name)
    {
        var mgr = RaptureAtkUnitManager.Instance();
        if (mgr is null) return null;

        var addon = mgr->GetAddonByName(name, 1);
        if (addon is null) return null;

        return addon->IsVisible ? addon : null;
    }

    /// <summary>在指定 Addon 里找「退出」按钮并触发。</summary>
    private bool TryFireExitButton(string addonName)
    {
        var unit = GetAddon(addonName);
        if (unit is null) return false;

        var nodeCount = unit->UldManager.NodeListCount;
        if (nodeCount <= 0) return false;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = unit->UldManager.NodeList[i];
            if (node is null) continue;

            // 按钮在 CS 里是 Component 类型
            if (node->Type is not NodeType.Component) continue;

            var compNode = (AtkComponentNode*)node;
            if (compNode->Component is null) continue;

            // 只处理按钮类组件
            var btn = (AtkComponentButton*)compNode->Component;
            if (btn is null) continue;

            var text = btn->ButtonTextNode is not null
                ? btn->ButtonTextNode->NodeText.ToString()
                : string.Empty;

            if (string.IsNullOrEmpty(text)) continue;

            if (!text.Contains("退出", StringComparison.Ordinal)
                && !text.Contains("离开", StringComparison.Ordinal))
                continue;

            // 触发按钮回调
            unit->FireCallbackInt((int)node->NodeId);
            log.Information("[TreasureLedger] 点击 {Addon} 的「{Text}」", addonName, text);
            return true;
        }

        return false;
    }

    /// <summary>点确认框的「是」。</summary>
    private bool TryClickYesno()
    {
        var unit = GetAddon(YesnoAddon);
        if (unit is null) return false;

        // SelectYesno 回调：0 = 是
        unit->FireCallbackInt(0);
        log.Information("[TreasureLedger] 确认框点了「是」");
        return true;
    }

    private void OnYesnoSetup(AddonEvent type, AddonArgs args)
    {
        if (!waitingForYesno) return;

        waitingForYesno = false;
        try
        {
            var unit = GetAddon(YesnoAddon);
            if (unit is null) return;

            unit->FireCallbackInt(0);
            LastResult = "已确认退出";
            log.Information("[TreasureLedger] 确认框弹出并已确认");
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 确认框处理失败");
        }
    }

    /// <summary>每帧检查等待超时。</summary>
    public void Tick()
    {
        if (!waitingForYesno) return;

        if ((DateTime.Now - pendingSince).TotalSeconds > 5)
        {
            waitingForYesno = false;
            LastResult = "确认框未出现，退出可能未生效";
            log.Warning("[TreasureLedger] 等待确认框超时");
        }
    }
}
