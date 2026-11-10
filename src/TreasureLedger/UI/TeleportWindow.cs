using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Teleport;

namespace TreasureLedger.UI;

/// <summary>传送提醒与自动确认设置页。</summary>
public sealed class TeleportWindow
{
    private readonly Configuration config;
    private readonly TeleportConfirm confirm;
    private readonly TreasurePartyDetector party;
    private readonly Action saveConfig;

    public TeleportWindow(
        Configuration config,
        TeleportConfirm confirm,
        TreasurePartyDetector party,
        Action saveConfig)
    {
        this.config = config;
        this.confirm = confirm;
        this.party = party;
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

        var inParty = party.InParty;
        var isTreasure = party.IsTreasureParty(config.Teleport.TreasurePartyWindowMinutes);

        ImGui.Text($"小队：{(inParty ? $"在队伍中（{party.PartySize} 人）" : "单人")}");
        ImGui.Text($"挖宝队伍判定：{(isTreasure ? "是" : "否")}");

        if (confirm.Armed)
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "自动确认：已启用");
        else
            ImGui.TextDisabled("自动确认：未启用");

        if (confirm.ConfirmCount > 0)
        {
            ImGui.TextDisabled($"已确认 {confirm.ConfirmCount} 次");
            if (confirm.LastPrompt.Length > 0)
                ImGui.TextWrapped("最近：" + confirm.LastPrompt);
        }
    }

    private void DrawSettings()
    {
        ImGui.TextDisabled("传送提醒");
        ImGui.Spacing();

        var cfg = config.Teleport;

        var notify = cfg.EnableNotify;
        if (ImGui.Checkbox("收到传送邀请时在聊天栏提醒##t1", ref notify))
        {
            cfg.EnableNotify = notify;
            saveConfig();
        }

        var auto = cfg.EnableAutoConfirm;
        if (ImGui.Checkbox("自动点击确认##t2", ref auto))
        {
            cfg.EnableAutoConfirm = auto;
            saveConfig();
        }

        if (cfg.EnableAutoConfirm)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), "会替你做决定，请确认场景");
        }

        var only = cfg.OnlyInTreasureParty;
        if (ImGui.Checkbox("只在挖宝队伍中生效##t3", ref only))
        {
            cfg.OnlyInTreasureParty = only;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("（防止误确认其他邀请）");

        var window = cfg.TreasurePartyWindowMinutes;
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderInt("判定窗口(分)##t4", ref window, 30, 480))
        {
            cfg.TreasurePartyWindowMinutes = window;
            saveConfig();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("进过宝库后，这段时间内视为仍在挖宝队伍");
    }

    private void DrawTest()
    {
        ImGui.TextDisabled("测试");

        if (ImGui.Button("对当前确认框点「是」##tt1"))
            confirm.ForceYes();

        ImGui.SameLine();
        if (ImGui.Button("查看识别规则##tt2"))
            ImGui.OpenPopup("##teleportrules");

        DrawRulesPopup();
    }

    private void DrawRulesPopup()
    {
        if (!ImGui.BeginPopup("##teleportrules")) return;

        ImGui.TextDisabled("识别为传送邀请的对话文本：");
        ImGui.Separator();
        ImGui.Text("中文：收到…传送邀请 / 去往…的传送邀请 / 传送…接受");
        ImGui.Text("日文：テレポ…受け / よろしい");
        ImGui.Text("英文：accept…teleport / teleport…invitation");
        ImGui.Spacing();
        ImGui.TextDisabled("匹配到才会自动点「是」，其他确认框不动。");

        ImGui.EndPopup();
    }
}
