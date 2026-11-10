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
        var isTreasure = party.IsTreasureParty(config.Teleport.TreasureEvidenceMinutes);

        ImGui.Text($"小队：{(inParty ? $"在队伍中（{party.PartySize} 人）" : "单人")}");

        // 招募信息（软证据）
        if (party.JoinedTitle.Length > 0 || party.JoinedDescription.Length > 0)
        {
            if (party.JoinedTitle.Length > 0)
                ImGui.Text($"招募标题：{party.JoinedTitle}");
            if (party.JoinedDescription.Length > 0)
                ImGui.TextWrapped($"招募简介：{party.JoinedDescription}");

            if (party.HasRecruitHint)
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.9f, 1f), $"招募提示：{party.RecruitHintReason}");
        }

        // 硬证据
        if (party.LastHardEvidence != DateTime.MinValue)
        {
            var ago = (int)(DateTime.Now - party.LastHardEvidence).TotalMinutes;
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f),
                $"挖宝证据：{party.LastEvidence}（{ago} 分钟前）");
            ImGui.TextDisabled($"累计 {party.HardEvidenceCount} 次");
        }

        ImGui.Text($"挖宝小队判定：{(isTreasure ? "是" : "否")}");
        ImGui.TextDisabled(party.VerdictText(config.Teleport.TreasureEvidenceMinutes));

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
        ImGui.TextDisabled("（需通过招募加入，且招募被判定为寻宝）");

        var window = cfg.TreasureEvidenceMinutes;
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderInt("证据有效期(分)##t4", ref window, 30, 480))
        {
            cfg.TreasureEvidenceMinutes = window;
            saveConfig();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("打过挖宝怪、进过宝库之后，这段时间内仍算挖宝小队");
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
