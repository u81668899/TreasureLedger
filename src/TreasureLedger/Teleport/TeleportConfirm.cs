using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TreasureLedger.Teleport;

/// <summary>
/// 传送邀请自动确认。
///
/// 挖宝时队友频繁发传送邀请，每次都要手点确认，很烦。
/// 这个模块在「检测到自己在寻宝队伍」时自动点「是」。
///
/// ══════════════════════════════════════════════════════════════
/// 实现方式（参考 YesAlready）
///
/// YesAlready 的做法：hook FireCallback，按对话文本匹配规则，
/// 命中就调 SelectYesno.Yes()。
///
/// 它内部最终落点是 CS 的 AtkUnitBase：
///     FireCallbackInt(thisPtr, callbackValue)
/// 其中 SelectYesno 的 callbackValue = 0 表示「是」，1 表示「否」。
///
/// 我们不需要 hook，只需要：
///   1. 监听 SelectYesno 出现（AddonLifecycle.PostSetup）
///   2. 读出对话文本
///   3. 文本匹配到「传送」相关就调 FireCallbackInt(0)
///
/// 这个途径完全基于 CS 官方函数，不依赖任何第三方插件。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed class TeleportConfirm : IDisposable
{
    private readonly IPluginLog log;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;

    private const string YesnoAddon = "SelectYesno";

    /// <summary>
    /// 传送邀请对话的正则。
    /// 中日英三语都覆盖，因为客户端语言可能不同。
    /// </summary>
    private static readonly Regex[] TeleportPatterns =
    {
        new(@"收到.*传送邀请", RegexOptions.Compiled),           // 中
        new(@"去往.*的传送邀请", RegexOptions.Compiled),         // 中，更具体
        new(@"传送.*(接受|同意|确认)", RegexOptions.Compiled),   // 中
        new(@"テレポ.*(受け|よろしい)", RegexOptions.Compiled),  // 日
        new(@"accept.*teleport", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"teleport.*invitation", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"travel to.*\?", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    /// <summary>是否启用（由外部根据「是否在寻宝队伍」控制）。</summary>
    public bool Armed { get; set; }

    /// <summary>最近一次触发的对话文本，给 UI 显示。</summary>
    public string LastPrompt { get; private set; } = string.Empty;

    /// <summary>累计自动确认次数。</summary>
    public int ConfirmCount { get; private set; }

    /// <summary>最近一次动作时间。</summary>
    public DateTime LastActionAt { get; private set; } = DateTime.MinValue;

    /// <summary>触发时回调（用于聊天栏提醒）。</summary>
    public event Action<string>? Confirmed;

    public TeleportConfirm(IPluginLog log, IAddonLifecycle addonLifecycle, IGameGui gameGui)
    {
        this.log = log;
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;

        // 监听确认框出现
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, YesnoAddon, OnYesnoSetup);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, YesnoAddon, OnYesnoSetup);
    }

    private unsafe void OnYesnoSetup(AddonEvent type, AddonArgs args)
    {
        if (!Armed) return;

        try
        {
            var addon = (AddonSelectYesno*)args.Addon.Address;
            if (addon is null) return;

            // 读对话文本
            var text = ReadPrompt(addon);
            if (string.IsNullOrWhiteSpace(text)) return;

            // 匹配传送相关
            if (!IsTeleportPrompt(text)) return;

            LastPrompt = text;
            ConfirmCount++;
            LastActionAt = DateTime.Now;

            // callbackValue = 0 → 点「是」
            ((AtkUnitBase*)addon)->FireCallbackInt(0);

            log.Information("[TreasureLedger] 已自动确认传送：{Text}", text);
            Confirmed?.Invoke(text);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 自动确认传送失败");
        }
    }

    /// <summary>从 SelectYesno 里读提示文本。</summary>
    private static unsafe string ReadPrompt(AddonSelectYesno* addon)
    {
        try
        {
            // AtkValues[0] 是提示文本
            var unit = (AtkUnitBase*)addon;
            if (unit->AtkValues is null || unit->AtkValuesCount == 0) return string.Empty;

            var val = unit->AtkValues[0];
            // AtkValue 里 type 为 7（String）时 string 有效
            if (val.Type != AtkValueType.String) return string.Empty;

            return val.String.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsTeleportPrompt(string text)
    {
        foreach (var r in TeleportPatterns)
        {
            try { if (r.IsMatch(text)) return true; }
            catch { }
        }
        return false;
    }

    /// <summary>手动测试：对当前确认框点「是」。</summary>
    public unsafe bool ForceYes()
    {
        try
        {
            var mgr = RaptureAtkUnitManager.Instance();
            if (mgr is null) return false;

            var unit = mgr->GetAddonByName(YesnoAddon, 1);
            if (unit is null || !unit->IsVisible) return false;

            unit->FireCallbackInt(0);
            ConfirmCount++;
            LastActionAt = DateTime.Now;
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 强制确认失败");
            return false;
        }
    }
}
