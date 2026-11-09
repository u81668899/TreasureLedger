using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Items;
using TreasureLedger.Loot;

namespace TreasureLedger.UI;

/// <summary>自动投掷设置页。</summary>
public sealed class AutoRollWindow
{
    private readonly Configuration config;
    private readonly AutoRoller roller;
    private readonly ItemCatalog catalog;
    private readonly Action saveConfig;
    private readonly Action onDataChanged;

    // 手动添加物品到名单
    private string addItemInput = string.Empty;
    private uint addItemResolved;
    private string addItemResolvedName = string.Empty;

    public AutoRollWindow(
        Configuration config,
        AutoRoller roller,
        ItemCatalog catalog,
        Action saveConfig,
        Action onDataChanged)
    {
        this.config = config;
        this.roller = roller;
        this.catalog = catalog;
        this.saveConfig = saveConfig;
        this.onDataChanged = onDataChanged;
    }

    public void Draw()
    {
        var cfg = config.AutoRoll;

        DrawMasterSwitch(cfg);
        ImGui.Separator();

        DrawBasicSettings(cfg);
        ImGui.Separator();

        DrawListSettings(cfg);
        ImGui.Separator();

        DrawItemList(cfg);
    }

    private void DrawMasterSwitch(AutoRollConfig cfg)
    {
        var enabled = cfg.Enabled;
        if (ImGui.Checkbox("启用自动投掷", ref enabled))
        {
            cfg.Enabled = enabled;
            saveConfig();
        }

        if (!cfg.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("（未启用时不会动游戏）");
        }

        // 状态行
        ImGui.TextDisabled(
            $"投掷函数：{(roller.Ready ? "已就绪" : "未就绪")}　" +
            $"已投：{roller.RolledCount} 次");

        if (roller.LastAction.Length > 0)
            ImGui.TextDisabled("最近：" + roller.LastAction);
    }

    private void DrawBasicSettings(AutoRollConfig cfg)
    {
        ImGui.TextDisabled("基本设置");
        ImGui.Spacing();

        var onlyDungeon = cfg.OnlyInTreasureDungeon;
        if (ImGui.Checkbox("仅在宝库副本内生效", ref onlyDungeon))
        {
            cfg.OnlyInTreasureDungeon = onlyDungeon;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("（关闭则任何副本都会投）");

        ImGui.SetNextItemWidth(140);
        var defaultPolicy = cfg.DefaultPolicy;
        if (DrawPolicyCombo("默认策略", "##default", ref defaultPolicy, allowDefault: false))
        {
            cfg.DefaultPolicy = defaultPolicy;
            saveConfig();
        }

        ImGui.SetNextItemWidth(140);
        var interval = cfg.IntervalMs;
        if (ImGui.SliderInt("投掷间隔(ms)", ref interval, 100, 2000))
        {
            cfg.IntervalMs = interval;
            saveConfig();
        }
    }

    private void DrawListSettings(AutoRollConfig cfg)
    {
        ImGui.TextDisabled("名单模式");
        ImGui.Spacing();

        var mode = cfg.ListMode;
        if (ImGui.RadioButton("白名单：名单内按指定策略", mode == RollListMode.Whitelist))
        {
            cfg.ListMode = RollListMode.Whitelist;
            saveConfig();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("黑名单：名单内一律放弃", mode == RollListMode.Blacklist))
        {
            cfg.ListMode = RollListMode.Blacklist;
            saveConfig();
        }

        if (cfg.ListMode == RollListMode.Whitelist)
        {
            ImGui.SetNextItemWidth(140);
            var wlPolicy = cfg.WhitelistPolicy;
            if (DrawPolicyCombo("白名单策略", "##wl", ref wlPolicy, allowDefault: false))
            {
                cfg.WhitelistPolicy = wlPolicy;
                saveConfig();
            }
        }
    }

    private void DrawItemList(AutoRollConfig cfg)
    {
        ImGui.TextDisabled("物品名单：单独指定每件物品的投掷方式");

        // 添加
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("物品名或ID##rolladd", ref addItemInput, 96);
        ImGui.SameLine();
        if (ImGui.SmallButton("查找##roll"))
            ResolveAdd();

        if (addItemResolved != 0)
        {
            ImGui.TextDisabled($"→ #{addItemResolved}  {addItemResolvedName}");
            ImGui.SameLine();
            if (ImGui.SmallButton("加入名单"))
            {
                if (!cfg.ItemPolicies.ContainsKey(addItemResolved))
                {
                    cfg.ItemPolicies[addItemResolved] = RollPolicy.Default;
                    saveConfig();
                    onDataChanged();
                }
                addItemInput = string.Empty;
                addItemResolved = 0;
                addItemResolvedName = string.Empty;
            }
        }

        ImGui.Spacing();

        // 名单表格
        if (cfg.ItemPolicies.Count == 0)
        {
            ImGui.TextDisabled("名单为空。");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("tl-autoroll", 4, flags, new Vector2(0, 240))) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("投掷方式", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        uint toRemove = 0;
        foreach (var kv in cfg.ItemPolicies.OrderBy(x => x.Key).ToList())
        {
            ImGui.TableNextRow();
            ImGui.PushID((int)kv.Key);

            ImGui.TableNextColumn();
            var name = catalog.GetName(kv.Key);
            ImGui.TextUnformatted(name.Length > 0 ? name : $"#{kv.Key}");

            ImGui.TableNextColumn();
            ImGui.TextDisabled(kv.Key.ToString());

            ImGui.TableNextColumn();
            var policy = kv.Value;
            if (DrawPolicyCombo("##p", "##pp", ref policy, allowDefault: true))
            {
                cfg.ItemPolicies[kv.Key] = policy;
                saveConfig();
            }

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("移除"))
                toRemove = kv.Key;

            ImGui.PopID();
        }

        ImGui.EndTable();

        if (toRemove != 0)
        {
            cfg.ItemPolicies.Remove(toRemove);
            saveConfig();
            onDataChanged();
        }
    }

    /// <summary>策略下拉。返回 true 表示改了。</summary>
    private static bool DrawPolicyCombo(string label, string id, ref RollPolicy policy, bool allowDefault)
    {
        var options = allowDefault
            ? new[] { RollPolicy.Default, RollPolicy.Need, RollPolicy.Greed, RollPolicy.Pass }
            : new[] { RollPolicy.Need, RollPolicy.Greed, RollPolicy.Pass };

        var names = options.Select(PolicyName).ToArray();
        var current = Array.IndexOf(options, policy);
        if (current < 0) current = 0;

        if (!ImGui.Combo(label + id, ref current, names, names.Length)) return false;

        policy = options[current];
        return true;
    }

    public static string PolicyName(RollPolicy p) => p switch
    {
        RollPolicy.Default => "跟默认",
        RollPolicy.Need => "需求",
        RollPolicy.Greed => "贪婪",
        RollPolicy.Pass => "放弃",
        _ => p.ToString(),
    };

    private void ResolveAdd()
    {
        addItemResolved = 0;
        addItemResolvedName = string.Empty;

        var input = addItemInput.Trim();
        if (input.Length == 0) return;

        // 纯数字当 ID
        if (uint.TryParse(input, out var id) && id > 0)
        {
            var name = catalog.GetName(id);
            if (name.Length > 0)
            {
                addItemResolved = id;
                addItemResolvedName = name;
                return;
            }
        }

        // 否则当名字
        var found = catalog.FindByName(input);
        if (found is not null)
        {
            addItemResolved = found.Value.ItemId;
            addItemResolvedName = found.Value.Name;
        }
    }
}
