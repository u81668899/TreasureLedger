using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreasureLedger.Items;
using TreasureLedger.Ledger;

namespace TreasureLedger.Report;

/// <summary>
/// 播报格式。复刻 Triggernometry XML 的输出，按宝图分类分行。
/// </summary>
public static class ReportFormatter
{
    /// <summary>内置 + 用户自定义，合并后的完整清单。应用了显示名覆盖。</summary>
    /// <param name="catalog">用于把自定义物品的 ID 换成游戏原名，可为 null。</param>
    public static List<TrackedItem> EffectiveItems(Configuration config, ItemCatalog? catalog = null)
    {
        var list = new List<TrackedItem>(TrackedItems.Builtin.Count);

        foreach (var b in TrackedItems.Builtin)
        {
            // 有覆盖就用覆盖名
            if (config.BuiltinDisplayOverrides.TryGetValue(b.ItemId, out var custom)
                && !string.IsNullOrWhiteSpace(custom))
            {
                list.Add(b with { Display = custom });
            }
            else
            {
                list.Add(b);
            }
        }

        foreach (var c in config.CustomItems)
        {
            if (c.ItemId == 0) continue;
            if (TrackedItems.FindBuiltin(c.ItemId) is not null) continue;
            if (list.Any(x => x.ItemId == c.ItemId)) continue;

            var gameName = catalog?.GetName(c.ItemId) ?? string.Empty;
            if (gameName.Length == 0) gameName = $"#{c.ItemId}";

            list.Add(new TrackedItem(
                c.ItemId,
                gameName,
                c.Display,
                c.Tier,
                UserAdded: true));
        }

        return list;
    }

    /// <summary>「金币：x　图数：x　洞数：x　底数：x」</summary>
    public static string BuildSummaryLine(SessionLedger ledger)
        => $"金币：{ledger.Gil:n0}　图数：{ledger.MapCount}　洞数：{ledger.VaultCount}　底数：{ledger.BottomCount}";

    /// <summary>层数一行：本次行程的最深层与底数。</summary>
    public static string BuildFloorLine(SessionLedger ledger)
    {
        var run = ledger.CurrentRun;
        if (run is null) return "本次：不在宝库";

        var cur = run.Current?.Floor ?? 0;
        var max = run.MaxFloor;
        var chests = run.TotalChests;

        var s = $"本次：{run.DungeonName}　当前{cur}层／最深{max}层";
        if (chests > 0) s += $"　开箱{chests}";
        if (run.BottomCount > 0) s += $"　下底{run.BottomCount}";
        return s;
    }

    /// <summary>神殿分级一行。G17-G18 用大梦/终梦命名。</summary>
    public static string BuildShrineLine(SessionLedger ledger)
    {
        var high = ledger.GetShrineTier(ShrineTier.High);
        var mid = ledger.GetShrineTier(ShrineTier.Middle);
        var low = ledger.GetShrineTier(ShrineTier.Low);

        if (high + mid + low == 0) return string.Empty;

        return ledger.UsesDreamNaming
            ? $"神殿：大梦{high}　中梦{mid}　终梦{low}"
            : $"神殿：上级{high}　中级{mid}　下级{low}";
    }

    /// <summary>按分类拼「本次掉落内容」，每个分类一行。自己添加的单独一行。</summary>
    public static List<string> BuildLootLines(SessionLedger ledger, Configuration config, ItemCatalog? catalog = null)
    {
        var lines = new List<string>();
        var items = EffectiveItems(config, catalog);

        // 内置物品：按宝图分类
        foreach (var tier in Enum.GetValues<MapTier>())
        {
            var bucket = items.Where(x => !x.UserAdded && x.Tier == tier).ToList();
            if (bucket.Count == 0) continue;

            var body = JoinItems(bucket, ledger, config);
            if (body.Length == 0) continue;

            lines.Add($"{Configuration.TierName(tier)}：{body}");
        }

        // 自己添加的：单独一行
        var customs = items.Where(x => x.UserAdded).ToList();
        if (customs.Count > 0)
        {
            var body = JoinItems(customs, ledger, config);
            if (body.Length > 0) lines.Add($"自己添加：{body}");
        }

        if (lines.Count == 0) lines.Add("似乎还没有值钱的掉落物…");
        return lines;
    }

    /// <summary>把一组物品拼成「名字×数量　名字×数量」。</summary>
    private static string JoinItems(List<TrackedItem> bucket, SessionLedger ledger, Configuration config)
    {
        var sb = new StringBuilder();
        foreach (var it in bucket)
        {
            if (!config.IsItemEnabled(it.ItemId)) continue;
            var n = ledger.GetCount(it.ItemId);
            if (n <= 0) continue;
            sb.Append(it.ReportName).Append('×').Append(n).Append('　');
        }
        return sb.ToString().TrimEnd('　');
    }

    /// <summary>发送用完整内容。</summary>
    public static List<string> BuildFullReport(SessionLedger ledger, Configuration config, string recruitText = "", ItemCatalog? catalog = null, string? mobSummary = null)
    {
        var lines = new List<string> { BuildSummaryLine(ledger) };

        var floor = BuildFloorLine(ledger);
        if (floor.Length > 0) lines.Add(floor);

        var shrine = BuildShrineLine(ledger);
        if (shrine.Length > 0) lines.Add(shrine);

        if (ledger.LastDutyName.Length > 0)
            lines.Add("当前：" + ledger.LastDutyName);

        if (!string.IsNullOrWhiteSpace(recruitText))
            lines.Add("招募：《" + recruitText.Trim() + "》");

        if (!string.IsNullOrWhiteSpace(mobSummary))
            lines.Add(mobSummary);

        lines.AddRange(BuildLootLines(ledger, config, catalog));
        return lines;
    }

    /// <summary>按来源拆分（UI 用）。</summary>
    public static IEnumerable<string> BuildSourceBreakdown(SessionLedger ledger, Configuration config, ItemCatalog? catalog = null)
    {
        foreach (var it in EffectiveItems(config, catalog))
        {
            var total = ledger.GetCount(it.ItemId);
            if (total <= 0) continue;

            var bySrc = ledger.ItemBySource.GetValueOrDefault(it.ItemId);
            var m = bySrc?.GetValueOrDefault(Loot.LootSource.Monster) ?? 0;
            var c = bySrc?.GetValueOrDefault(Loot.LootSource.Chest) ?? 0;
            var r = bySrc?.GetValueOrDefault(Loot.LootSource.Roll) ?? 0;

            yield return $"[{Configuration.TierName(it.Tier)}] {it.ReportName}×{total}　小怪{m}/宝箱{c}/投掷{r}";
        }
    }
}
