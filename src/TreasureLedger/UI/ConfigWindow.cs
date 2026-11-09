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

public sealed class ConfigWindow
{
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly DungeonTracker tracker;
    private readonly ItemCatalog catalog;
    private readonly Action saveConfig;
    private readonly Action resetLedger;
    private readonly Action<string> sendToChat;
    private readonly Action<bool> setPreview;
    private readonly Action onDataChanged;
    private readonly Func<string> recruitTextProvider;
    private readonly Action<string> setRecruit;

    // 添加自定义物品的输入缓冲
    private string newItemId = string.Empty;
    private string newItemName = string.Empty;
    private int newItemTier = 5;

    public ConfigWindow(
        Configuration config,
        SessionLedger ledger,
        DungeonTracker tracker,
        ItemCatalog catalog,
        Action saveConfig,
        Action resetLedger,
        Action<string> sendToChat,
        Action<bool> setPreview,
        Action onDataChanged,
        Func<string> recruitTextProvider,
        Action<string> setRecruit)

    {
        this.config = config;
        this.ledger = ledger;
        this.tracker = tracker;
        this.catalog = catalog;
        this.saveConfig = saveConfig;
        this.resetLedger = resetLedger;
        this.sendToChat = sendToChat;
        this.setPreview = setPreview;
        this.onDataChanged = onDataChanged;
        this.recruitTextProvider = recruitTextProvider;
        this.setRecruit = setRecruit;

    }

    public void DrawBody()
    {
        DrawHeader();
        ImGui.Separator();

        if (!ImGui.BeginTabBar("tl-tabs")) return;

        if (ImGui.BeginTabItem("指令")) { DrawCommands(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("频道")) { DrawChannels(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("物品")) { DrawItems(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("统计")) { DrawStats(); ImGui.EndTabItem(); }

        ImGui.EndTabBar();
    }

    private void DrawHeader()
    {
        var dg = tracker.CurrentDungeon;
        ImGui.Text($"区域：{(dg?.Name ?? "不在宝库")}　TT={tracker.CurrentTerritory}");
        ImGui.Text($"金币 {ledger.Gil:n0}　图数 {ledger.MapCount}　洞数 {ledger.VaultCount}　底数 {ledger.BottomCount}");

        if (ImGui.Button("查询到默语"))
            sendToChat("/e " + ReportFormatter.BuildSummaryLine(ledger));
        ImGui.SameLine();
        if (ImGui.Button("重置"))
            resetLedger();
    }

    private void DrawCommands()
    {
        ImGui.TextDisabled("在聊天栏直接输入这些指令即可触发。哪个频道发的，就回哪个频道。");
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
        ImGui.Separator();

        var join = config.ResetOnPartyJoin;
        if (ImGui.Checkbox("加入小队 / 开招募时自动重置", ref join))
        {
            config.ResetOnPartyJoin = join;
            saveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("招募内容（会随统计一起播报）");

        var rc = recruitTextProvider();
        if (ImGui.InputText("招募", ref rc, 128))
        {
            setRecruit(rc);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("清空"))
        {
            setRecruit(string.Empty);
        }

        var print = config.PrintLootLocally;
        if (ImGui.Checkbox("掉落时本地打印（调试）", ref print))
        {
            config.PrintLootLocally = print;
            saveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("频道发送");

        var preview = config.ChannelPreviewOnly;
        if (ImGui.Checkbox("仅本地预览（不真的发到频道）", ref preview))
        {
            config.ChannelPreviewOnly = preview;
            setPreview(preview);
            saveConfig();
        }

        if (config.ChannelPreviewOnly)
        {
            ImGui.TextDisabled("输出只显示在你自己的聊天栏，前缀标明目标频道。");
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.3f, 1f));
            ImGui.TextWrapped("⚠ 已启用真实频道发送。走的是 Dalamud 官方 Print(XivChatEntry)，" +
                              "但请先在安全场景测试一次，确认稳定再用于挖宝。");
            ImGui.PopStyleColor();
        }
    }

    private void DrawChannels()
    {
        ImGui.TextDisabled("勾选允许响应查询的频道。默认只有默语。");
        ImGui.Spacing();

        foreach (var c in Enum.GetValues<ReportChannel>())
        {
            var on = config.IsListenChannel(c);
            if (ImGui.Checkbox($"{Configuration.ChannelName(c)}　{config.ChannelSlash(c).Trim()}", ref on))
            {
                config.SetListenChannel(c, on);
                saveConfig();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        var l = config.LocalLinkshellIndex;
        if (ImGui.SliderInt("本服贝序号", ref l, 1, 8))
        {
            config.LocalLinkshellIndex = l;
            saveConfig();
        }

        var cl = config.CrossLinkshellIndex;
        if (ImGui.SliderInt("跨服贝序号", ref cl, 1, 8))
        {
            config.CrossLinkshellIndex = cl;
            saveConfig();
        }
    }

    private void DrawItems()
    {
        ImGui.TextDisabled("取消勾选 = 不统计。按宝图分类分组。");
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

        if (!ImGui.BeginChild("tl-items", new Vector2(0, -110), true)) return;

        // 内置物品，按分类
        foreach (var (tier, items) in TrackedItems.ByTier())
        {
            if (!ImGui.CollapsingHeader($"{Configuration.TierName(tier)}  ({items.Count})##tier{(int)tier}"))
                continue;

            foreach (var it in items)
            {
                var enabled = config.IsItemEnabled(it.ItemId);
                if (ImGui.Checkbox($"{it.Display}　{it.FullName}##i{it.ItemId}", ref enabled))
                {
                    config.ToggleItem(it.ItemId);
                    saveConfig();
                }
            }
        }

        // 用户自定义
        if (config.CustomItems.Count > 0)
        {
            if (ImGui.CollapsingHeader($"自己添加 ({config.CustomItems.Count})##custom"))
            {
                foreach (var c in config.CustomItems.ToList())
                {
                    var enabled = config.IsItemEnabled(c.ItemId);
                    if (ImGui.Checkbox($"[{Configuration.TierName(c.Tier)}] {c.Display}　#{c.ItemId}##c{c.ItemId}", ref enabled))
                    {
                        config.ToggleItem(c.ItemId);
                        saveConfig();
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"删除##d{c.ItemId}"))
                    {
                        config.CustomItems.Remove(c);
                        config.DisabledItemIds.Remove(c.ItemId);
                        saveConfig();
                    }
                }
            }
        }

        ImGui.EndChild();

        // 添加表单
        ImGui.Separator();
        ImGui.TextDisabled("添加物品（新版本自己补，不用等插件更新）");

        ImGui.SetNextItemWidth(120);
        ImGui.InputText("物品ID", ref newItemId, 16);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("显示名", ref newItemName, 32);

        var tiers = Enum.GetValues<MapTier>();
        var tierNames = tiers.Select(Configuration.TierName).ToArray();
        ImGui.SetNextItemWidth(160);
        ImGui.Combo("分类", ref newItemTier, tierNames, tierNames.Length);

        if (ImGui.Button("添加"))
        {
            if (uint.TryParse(newItemId.Trim(), out var id) && id > 0)
            {
                var name = newItemName.Trim();
                if (name.Length == 0)
                    name = catalog.GetName(id);

                if (!config.CustomItems.Any(x => x.ItemId == id))
                {
                    config.CustomItems.Add(new CustomItem
                    {
                        ItemId = id,
                        Display = name.Length > 0 ? name : $"#{id}",
                        Tier = tiers[Math.Clamp(newItemTier, 0, tiers.Length - 1)],
                        UserAdded = true,
                    });
                    saveConfig();
                    newItemId = string.Empty;
                    newItemName = string.Empty;
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("从本地物品表查名字"))
        {
            if (uint.TryParse(newItemId.Trim(), out var id))
                newItemName = catalog.GetName(id);
        }
    }

    private void DrawStats()
    {
        ImGui.TextDisabled("本次会话统计（按来源拆分）");
        ImGui.Separator();

        if (!ImGui.BeginChild("tl-stats", new Vector2(0, 0), true)) return;

        var any = false;
        foreach (var line in ReportFormatter.BuildSourceBreakdown(ledger, config))
        {
            ImGui.Text(line);
            any = true;
        }
        if (!any) ImGui.TextDisabled("还没有掉落记录。");

        ImGui.EndChild();
    }
}
