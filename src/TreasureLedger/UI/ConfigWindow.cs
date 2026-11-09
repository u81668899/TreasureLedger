using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TreasureLedger.Commands;
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

    // 解析后的物品（名字或 ID 输入都会归一到这两个字段）
    private uint resolvedItemId;
    private string resolvedItemName = string.Empty;

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

        DrawCommandRow("查询指令", "##q",
            config.QueryCommand, v => config.QueryCommand = v,
            config.QueryCommandIsRegex, v => config.QueryCommandIsRegex = v);

        DrawCommandRow("重置指令", "##r",
            config.ResetCommand, v => config.ResetCommand = v,
            config.ResetCommandIsRegex, v => config.ResetCommandIsRegex = v);

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

    /// <summary>
    /// 指令输入行：输入框 + 正则开关 + 校验底色。
    /// 正则合法时输入框底色绿色，非法时红色。
    /// </summary>
    private void DrawCommandRow(
        string label,
        string idSuffix,
        string value,
        Action<string> setValue,
        bool isRegex,
        Action<bool> setRegex)
    {
        // 正则开关放在左侧
        var regex = isRegex;
        if (ImGui.Checkbox("正则" + idSuffix, ref regex))
        {
            setRegex(regex);
            saveConfig();
        }
        ImGui.SameLine();

        var state = ChatCommandListener.CheckPattern(value, isRegex);

        // 根据校验结果选底色
        var hasColor = false;
        if (state == CommandMatchState.Invalid)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.55f, 0.15f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.65f, 0.20f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.75f, 0.25f, 0.25f, 1f));
            hasColor = true;
        }
        else if (state == CommandMatchState.Valid && isRegex)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.45f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.55f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.25f, 0.65f, 0.30f, 1f));
            hasColor = true;
        }

        ImGui.SetNextItemWidth(240);
        var buf = value;
        if (ImGui.InputText(label + idSuffix, ref buf, 128))
        {
            setValue(buf);
            saveConfig();
        }

        if (hasColor) ImGui.PopStyleColor(3);

        // 非法时补一句说明
        if (state == CommandMatchState.Invalid)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "正则语法错误");
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

        ImGui.Spacing();
        ImGui.TextDisabled("悄悄话回给谁：留空则自动回给最近发消息给你的人");

        var tell = config.TellTargetName;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("悄悄话对象", ref tell, 32))
        {
            config.TellTargetName = tell;
            saveConfig();
        }
    }

    private void DrawItems()
    {
        ImGui.TextDisabled("取消勾选 = 不统计。按宝图分类分组。第二列可改统计显示名。");
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

        if (!ImGui.BeginChild("tl-items", new Vector2(0, -150), true)) return;

        // 内置物品，按分类
        // 注意：折叠面板的 ID 必须固定，所以用 ### 而不是 ##。
        // ## 只是把后半段从显示里隐藏，整串仍然参与 ID 哈希；
        // ### 之后的字符串才完全决定 ID。
        foreach (var (tier, items) in TrackedItems.ByTier())
        {
            if (!ImGui.CollapsingHeader($"{Configuration.TierName(tier)}  ({items.Count})###tier{(int)tier}"))
                continue;

            DrawItemTable($"tier{(int)tier}", items, allowDelete: false);
        }

        // 用户自定义
        if (config.CustomItems.Count > 0)
        {
            if (ImGui.CollapsingHeader($"自己添加 ({config.CustomItems.Count})###custom"))
            {
                var customs = config.CustomItems
                    .Select(c => new TrackedItem(
                        c.ItemId,
                        catalog.GetName(c.ItemId) is { Length: > 0 } n ? n : $"#{c.ItemId}",
                        c.Display,
                        c.Tier,
                        UserAdded: true))
                    .ToList();

                DrawItemTable("custom", customs, allowDelete: true);
            }
        }

        ImGui.EndChild();

        // 添加表单
        ImGui.Separator();
        ImGui.TextDisabled("添加物品：输入物品名或物品 ID 都可以，会自动匹配另一个");

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("物品名或ID", ref newItemId, 96);

        ImGui.SameLine();
        if (ImGui.SmallButton("查找"))
        {
            ResolveItemInput();
        }

        // 解析结果提示
        if (resolvedItemId != 0)
        {
            ImGui.TextDisabled($"→ #{resolvedItemId}  {resolvedItemName}");
        }
        else if (newItemId.Trim().Length > 0)
        {
            ImGui.TextDisabled("→ 未找到匹配物品");
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("统计显示名（留空=用游戏原名）", ref newItemName, 64);

        if (ImGui.Button("添加"))
        {
            AddResolvedItem();
        }

        ImGui.SameLine();
        if (ImGui.Button("清空"))
        {
            newItemId = string.Empty;
            newItemName = string.Empty;
            resolvedItemId = 0;
            resolvedItemName = string.Empty;
        }
    }

    /// <summary>
    /// 绘制物品表格：勾选 / 自定义名 / 游戏原名 / ID / 删除。
    /// </summary>
    private void DrawItemTable(string tableId, List<TrackedItem> items, bool allowDelete)
    {
        var cols = allowDelete ? 5 : 4;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable($"tl-itemtbl-{tableId}", cols, flags)) return;

        ImGui.TableSetupColumn("统计", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("自定义名", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("游戏原名", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
        if (allowDelete) ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 50);

        ImGui.TableHeadersRow();

        // 删除要延迟到表格画完后再执行，
        // 否则中途改集合会让 ImGui 的折叠状态错乱。
        uint toDelete = 0;

        foreach (var it in items)
        {
            ImGui.TableNextRow();
            ImGui.PushID((int)it.ItemId);

            // 1) 勾选
            ImGui.TableNextColumn();
            var enabled = config.IsItemEnabled(it.ItemId);
            if (ImGui.Checkbox("##chk", ref enabled))
            {
                config.ToggleItem(it.ItemId);
                saveConfig();
            }

            // 2) 自定义名（可编辑）
            ImGui.TableNextColumn();
            var display = GetDisplay(it.ItemId, it.UserAdded);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##disp", ref display, 64))
                SetDisplay(it.ItemId, display, it.UserAdded);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("留空则播报时用游戏原名");

            // 3) 游戏原名
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(it.FullName);

            // 4) ID
            ImGui.TableNextColumn();
            ImGui.TextDisabled(it.ItemId.ToString());

            // 5) 删除（仅自己添加的）
            if (allowDelete)
            {
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("删除##del"))
                    toDelete = it.ItemId;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();

        // 表格画完再删，下一帧自然显示新数据
        if (toDelete != 0)
        {
            config.CustomItems.RemoveAll(x => x.ItemId == toDelete);
            config.DisabledItemIds.Remove(toDelete);
            saveConfig();
            onDataChanged();
        }
    }

    /// <summary>取某物品当前的自定义显示名。</summary>
    private string GetDisplay(uint itemId, bool userAdded)
    {
        if (userAdded)
        {
            var c = config.CustomItems.FirstOrDefault(x => x.ItemId == itemId);
            return c?.Display ?? string.Empty;
        }

        var builtin = TrackedItems.FindBuiltin(itemId);
        return builtin?.Display ?? string.Empty;
    }

    /// <summary>设置自定义显示名。空的就当作「用游戏原名」。</summary>
    private void SetDisplay(uint itemId, string display, bool userAdded)
    {
        var t = (display ?? string.Empty).Trim();

        if (userAdded)
        {
            var c = config.CustomItems.FirstOrDefault(x => x.ItemId == itemId);
            if (c is null) return;
            c.Display = t;
            saveConfig();
            return;
        }

        // 内置物品：把自定义名存进 CustomDisplay 覆盖表
        config.BuiltinDisplayOverrides.TryGetValue(itemId, out var old);
        if (t.Length == 0)
        {
            if (config.BuiltinDisplayOverrides.ContainsKey(itemId))
            {
                config.BuiltinDisplayOverrides.Remove(itemId);
                saveConfig();
            }
        }
        else if (old != t)
        {
            config.BuiltinDisplayOverrides[itemId] = t;
            saveConfig();
        }
    }

    /// <summary>把输入框内容解析成物品 ID（支持名字或 ID 两种输入）。</summary>
    private void ResolveItemInput()
    {
        resolvedItemId = 0;
        resolvedItemName = string.Empty;

        var input = newItemId.Trim();
        if (input.Length == 0) return;

        // 输入是纯数字 → 当 ID 查名字
        if (uint.TryParse(input, out var id) && id > 0)
        {
            var name = catalog.GetName(id);
            if (name.Length > 0)
            {
                resolvedItemId = id;
                resolvedItemName = name;
                return;
            }
        }

        // 否则当名字查 ID
        var found = catalog.FindByName(input);
        if (found is not null)
        {
            resolvedItemId = found.Value.ItemId;
            resolvedItemName = found.Value.Name;
        }
    }

    private void AddResolvedItem()
    {
        if (resolvedItemId == 0)
            ResolveItemInput();

        if (resolvedItemId == 0) return;
        if (config.CustomItems.Any(x => x.ItemId == resolvedItemId)) return;
        if (TrackedItems.FindBuiltin(resolvedItemId) is not null) return;

        config.CustomItems.Add(new CustomItem
        {
            ItemId = resolvedItemId,
            Display = newItemName.Trim(),   // 留空则播报时回退到游戏原名
            Tier = MapTier.G17G18,          // 自己添加的统一归「自己添加」组，不用分类
            UserAdded = true,
        });

        saveConfig();

        newItemId = string.Empty;
        newItemName = string.Empty;
        resolvedItemId = 0;
        resolvedItemName = string.Empty;
    }

    private void DrawStats()
    {
        ImGui.TextDisabled("本次会话统计（按来源拆分）");
        ImGui.Separator();

        if (!ImGui.BeginChild("tl-stats", new Vector2(0, 0), true)) return;

        var any = false;
        foreach (var line in ReportFormatter.BuildSourceBreakdown(ledger, config, catalog))
        {
            ImGui.Text(line);
            any = true;
        }
        if (!any) ImGui.TextDisabled("还没有掉落记录。");

        ImGui.EndChild();
    }
}
