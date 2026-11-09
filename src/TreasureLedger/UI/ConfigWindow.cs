using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TreasureLedger.Items;
using TreasureLedger.Ledger;
using TreasureLedger.Maps;
using TreasureLedger.Report;

namespace TreasureLedger.UI;

/// <summary>主配置窗口。</summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly DungeonTracker tracker;
    private readonly Action saveConfig;
    private readonly Action resetLedger;
    private readonly Action rebindCommands;
    private readonly Action<string> sendToChat;

    public ConfigWindow(
        Configuration config,
        SessionLedger ledger,
        DungeonTracker tracker,
        Action saveConfig,
        Action resetLedger,
        Action rebindCommands,
        Action<string> sendToChat)
        : base("Treasure Ledger###tl-config", ImGuiWindowFlags.NoScrollbar)
    {
        this.config = config;
        this.ledger = ledger;
        this.tracker = tracker;
        this.saveConfig = saveConfig;
        this.resetLedger = resetLedger;
        this.rebindCommands = rebindCommands;
        this.sendToChat = sendToChat;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(520, 620);
    }

    public void Dispose() { }

    public override void Draw()
    {
        // ── 状态 ──
        ImGui.TextDisabled("当前状态");
        ImGui.Separator();

        var dg = tracker.CurrentDungeon;
        ImGui.Text($"区域：{(dg?.Name ?? "不在宝库")}  (TT={tracker.CurrentTerritory})");
        ImGui.Text($"金币：{ledger.Gil:n0}　图数：{ledger.MapCount}　洞数：{ledger.VaultCount}　底数：{ledger.BottomCount}");
        ImGui.Spacing();

        if (ImGui.Button("查询统计"))
        {
            foreach (var line in ReportFormatter.BuildFullReport(ledger, config.DisabledSet()))
                sendToChat("/e " + line);
        }
        ImGui.SameLine();
        if (ImGui.Button("重置统计"))
        {
            resetLedger();
        }
        ImGui.Spacing();

        if (!ImGui.BeginTabBar("tl-tabs")) return;

        if (ImGui.BeginTabItem("指令"))
        {
            DrawCommands();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("频道"))
        {
            DrawChannels();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("物品"))
        {
            DrawItems();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("统计"))
        {
            DrawStats();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCommands()
    {
        ImGui.TextDisabled("指令可自定义，不需要 / 前缀。");
        ImGui.Spacing();

        var q = config.QueryCommand;
        if (ImGui.InputText("查询指令", ref q, 64))
        {
            config.QueryCommand = q;
            saveConfig();
        }

        var r = config.ResetCommand;
        if (ImGui.InputText("重置指令", ref r, 64))
        {
            config.ResetCommand = r;
            saveConfig();
        }

        ImGui.Spacing();
        if (ImGui.Button("应用指令修改"))
        {
            rebindCommands();
            saveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();

        var resetOnJoin = config.ResetOnPartyJoin;
        if (ImGui.Checkbox("加入小队/开招募时自动重置", ref resetOnJoin))
        {
            config.ResetOnPartyJoin = resetOnJoin;
            saveConfig();
        }

        var printLocal = config.PrintLootLocally;
        if (ImGui.Checkbox("掉落时在本地打印（调试）", ref printLocal))
        {
            config.PrintLootLocally = printLocal;
            saveConfig();
        }
    }

    private void DrawChannels()
    {
        ImGui.TextDisabled("勾选需要播报的频道。默认只有默语。");
        ImGui.Spacing();

        foreach (var c in Enum.GetValues<ReportChannel>())
        {
            var on = config.IsChannelEnabled(c);
            if (ImGui.Checkbox(Configuration.ChannelName(c) + "  " + config.ChannelSlash(c), ref on))
            {
                config.SetChannel(c, on);
                saveConfig();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        var lIdx = config.LocalLinkshellIndex;
        if (ImGui.SliderInt("本服贝序号", ref lIdx, 1, 8))
        {
            config.LocalLinkshellIndex = lIdx;
            saveConfig();
        }

        var cIdx = config.CrossLinkshellIndex;
        if (ImGui.SliderInt("跨服贝序号", ref cIdx, 1, 8))
        {
            config.CrossLinkshellIndex = cIdx;
            saveConfig();
        }
    }
    private void DrawItems()
    {
        ImGui.TextDisabled("取消勾选 = 不统计该物品。默认全选。");
        ImGui.Spacing();

        if (ImGui.Button("全选"))
        {
            config.DisabledItemIds.Clear();
            saveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button("全不选"))
        {
            config.DisabledItemIds.Clear();
            config.DisabledItemIds.AddRange(TrackedItems.AllIds());
            saveConfig();
        }

        ImGui.Separator();

        if (!ImGui.BeginChild("tl-item-list", new Vector2(0, 0), true)) return;

        foreach (var it in TrackedItems.All)
        {
            var enabled = config.IsItemEnabled(it.ItemId);
            if (ImGui.Checkbox($"{it.Display}  ({it.FullName})##{it.ItemId}", ref enabled))
            {
                config.ToggleItem(it.ItemId);
                saveConfig();
            }
        }

        ImGui.EndChild();
    }

    private void DrawStats()
    {
        ImGui.TextDisabled("本次会话统计（按来源拆分）");
        ImGui.Separator();

        var any = false;
        foreach (var line in ReportFormatter.BuildSourceBreakdown(ledger))
        {
            ImGui.Text(line);
            any = true;
        }

        if (!any) ImGui.TextDisabled("还没有掉落记录。");
    }
}
