using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Chat;
using TreasureLedger.Diagnostics;

namespace TreasureLedger.UI;

/// <summary>
/// 调试面板。
/// 用来排查 UI 结构与原生命令执行，正式使用时可以不管它。
/// </summary>
public sealed class DebugWindow
{
    private readonly AddonDumper dumper;
    private readonly GameCommandExecutor executor;
    private readonly Maps.DutyExiter dutyExiter;

    private string addonName = "ContentsFinder";
    private string dumpResult = string.Empty;
    private string commandInput = string.Empty;

    public DebugWindow(AddonDumper dumper, GameCommandExecutor executor, Maps.DutyExiter dutyExiter)
    {
        this.dumper = dumper;
        this.executor = executor;
        this.dutyExiter = dutyExiter;
    }

    public void Draw()
    {
        ImGui.TextDisabled("原生命令执行");
        ImGui.TextDisabled(
            $"状态：{(executor.Ready ? "已就绪" : "未就绪")}　已执行 {executor.ExecutedCount} 次");

        if (executor.LastResult.Length > 0)
            ImGui.TextDisabled("最近：" + executor.LastResult);

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("##cmd", ref commandInput, 256);
        ImGui.SameLine();
        if (ImGui.Button("执行##cmdrun"))
        {
            executor.Execute(commandInput);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("dutyfinder##quick"))
        {
            executor.Execute("/dutyfinder");
        }

        ImGui.Separator();

        ImGui.TextDisabled("退本测试（不会影响正常统计）");
        ImGui.TextDisabled(
            $"已发送 {dutyExiter.LeaveCount} 次　{(dutyExiter.LastResult.Length > 0 ? dutyExiter.LastResult : "")}");

        if (ImGui.Button("测试退本##leave"))
            dutyExiter.RequestExit();

        ImGui.SameLine();
        if (ImGui.SmallButton("不活跃退本##leaveinactive"))
            dutyExiter.RequestExit(inactive: true);

        ImGui.Separator();

        ImGui.TextDisabled("Addon 结构查看");
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##addon", ref addonName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Dump##dumpbtn"))
        {
            dumpResult = dumper.DumpAddonButtons(addonName);
        }

        ImGui.SameLine();
        if (ImGui.Button("列出可见##visible"))
        {
            dumpResult = dumper.DumpVisibleAddons();
        }

        ImGui.Separator();

        if (!ImGui.BeginChild("##dumpout", new Vector2(0, 0), true)) return;

        if (dumpResult.Length == 0)
            ImGui.TextDisabled("点上面的按钮查看结果。");
        else
            ImGui.TextUnformatted(dumpResult);

        ImGui.EndChild();
    }
}
