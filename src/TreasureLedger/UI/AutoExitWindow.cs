using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Maps;

namespace TreasureLedger.UI;

/// <summary>
/// 自动退出设置页。
/// 与自动投掷完全独立 —— 两者的开关、逻辑、循环都分开。
/// </summary>
public sealed class AutoExitWindow
{
    private readonly Configuration config;
    private readonly AutoExiter exiter;
    private readonly DutyExiter dutyExiter;
    private readonly VaultProbe probe;
    private readonly Action saveConfig;

    public AutoExitWindow(
        Configuration config,
        AutoExiter exiter,
        DutyExiter dutyExiter,
        VaultProbe probe,
        Action saveConfig)
    {
        this.config = config;
        this.exiter = exiter;
        this.dutyExiter = dutyExiter;
        this.probe = probe;
        this.saveConfig = saveConfig;
    }

    public void Draw()
    {
        DrawStatus();
        ImGui.Separator();
        DrawSettings();
        ImGui.Separator();
        DrawTest();
    }

    private void DrawStatus()
    {
        ImGui.TextDisabled("当前状态");
        ImGui.Spacing();

        var ecfg = config.AutoExit;

        if (!ecfg.Enabled)
        {
            ImGui.TextDisabled("自动退出：未启用");
            return;
        }

        // 实时探测
        int chests = 0, pending = 0;
        try
        {
            chests = probe.CountUnopenedChests();
            pending = probe.CountPendingRolls();
        }
        catch { }

        ImGui.Text($"未开宝箱：{chests}");
        ImGui.Text($"待投掷：{pending}");

        var state = exiter.State;
        var color = state switch
        {
            AutoExitState.Waiting => new Vector4(1f, 0.8f, 0.3f, 1f),
            AutoExitState.Exited => new Vector4(0.4f, 0.9f, 0.4f, 1f),
            _ => new Vector4(0.65f, 0.65f, 0.65f, 1f),
        };

        var label = state switch
        {
            AutoExitState.Waiting => "等待中",
            AutoExitState.Exited => "已退出",
            _ => "待机",
        };

        ImGui.TextColored(color, $"状态：{label}");

        if (exiter.LastReason.Length > 0)
            ImGui.TextDisabled(exiter.LastReason);

        ImGui.TextDisabled($"退本命令已发送 {dutyExiter.LeaveCount} 次");
    }

    private void DrawSettings()
    {
        var ecfg = config.AutoExit;

        var enabled = ecfg.Enabled;
        if (ImGui.Checkbox("启用自动退出##exit", ref enabled))
        {
            ecfg.Enabled = enabled;
            saveConfig();
        }

        if (!ecfg.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("（未启用时不会退本）");
            return;
        }

        ImGui.Spacing();

        // 退本可用性（CS 特征码预检）
        if (!dutyExiter.Available)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "退本功能不可用");
            ImGui.TextWrapped(dutyExiter.UnavailableReason);
            ImGui.Spacing();
        }

        var requireBottom = ecfg.RequireBottom;
        if (ImGui.Checkbox("必须下底过才退出##eb", ref requireBottom))
        {
            ecfg.RequireBottom = requireBottom;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("（防止普通进洞误触发）");

        var requireRolls = ecfg.RequireRollsFinished;
        if (ImGui.Checkbox("必须投掷全部结束才退出##er", ref requireRolls))
        {
            ecfg.RequireRollsFinished = requireRolls;
            saveConfig();
        }

        var wait = ecfg.WaitSeconds;
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderInt("退出前等待(秒)##ew", ref wait, 3, 60))
        {
            ecfg.WaitSeconds = wait;
            saveConfig();
        }

        var notify = ecfg.NotifyBeforeExit;
        if (ImGui.Checkbox("退出前在聊天栏提示##en", ref notify))
        {
            ecfg.NotifyBeforeExit = notify;
            saveConfig();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("退出条件：下底过 + 场上无未开宝箱 + 无待投掷物品");
    }

    private void DrawTest()
    {
        ImGui.TextDisabled("手动测试");

        if (ImGui.Button("立即退出副本##exittest"))
        {
            dutyExiter.RequestExit();
        }

        ImGui.SameLine();
        if (ImGui.Button("重置退出状态##exitreset"))
        {
            exiter.Reset();
        }

        if (dutyExiter.LastResult.Length > 0)
            ImGui.TextDisabled("最近：" + dutyExiter.LastResult);

        if (!dutyExiter.Available)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("重新预检##exitprobe"))
                dutyExiter.Recheck();
        }
    }
}
