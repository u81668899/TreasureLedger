using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreasureLedger.Items;
using TreasureLedger.Ledger;

namespace TreasureLedger.Report;

/// <summary>
/// 播报格式。复刻 Triggernometry XML 的输出，但按宝图分类分行。
/// </summary>
public static class ReportFormatter
{
    /// <summary>内置 + 用户自定义，合并后的完整清单。</summary>
    public static List<TrackedItem> EffectiveItems(Configuration config)
    {
        var list = new List<TrackedItem>(TrackedItems.Builtin);
        foreach (var c in config.CustomItems)
        {
            if (c.ItemId == 0) continue;
            // 已存在的内置物品不重复添加
            if (TrackedItems.FindBuiltin(c.ItemId) is not null) continue;
            if (list.Any(x => x.ItemId == c.ItemId)) continue;

            list.Add(new TrackedItem(
                c.ItemId,
                c.Display.Length > 0 ? c.Display : $"#{c.ItemId}",
                c.Display.Length > 0 ? c.Display : $"#{c.ItemId}",
                c.Tier,
                UserAdded: true));
        }
        return list;
    }

    /// <summary>
    /// 按分类拼「本次掉落内容」。
    /// 每个分类一行，只列数量大于 0 且未被关闭的物品。
    /// </summary>
    public static List<string> BuildLootLines(SessionLedger ledger, Configuration config)
    {
        var lines = new List<string>();
        var items = EffectiveItems(config);

        foreach (var tier in Enum.GetValues<MapTier>())
        {
            var bucket = items.Where(x => x.Tier == tier).ToList();
            if (bucket.Count == 0) continue;

            var sb = new StringBuilder();
            foreach (var it in bucket)
            {
                if (!config.IsItemEnabled(it.ItemId)) continue;
                var n = ledger.GetCount(it.ItemId);
                if (n <= 0) continue;
                sb.Append(it.Display).Append('×').Append(n).Append('　');
            }

            if (sb.Length == 0) continue;

            var body = sb.ToString().TrimEnd('　');
            lines.Add($"{Configuration.TierName(tier)}：{body}");
        }

        if (lines.Count == 0)
            lines.Add("似乎还没有值钱的掉落物…");

        return lines;
    }

    /// <summary>「金币：x　图数：x　洞数：x　底数：x」</summary>
    public static string BuildSummaryLine(SessionLedger ledger)
        => $"金币：{ledger.Gil:n0}　图数：{ledger.MapCount}　洞数：{ledger.VaultCount}　底数：{ledger.BottomCount}";

    /// <summary>发送用完整内容：概要一行 + 分类若干行。</summary>
    public static List<string> BuildFullReport(SessionLedger ledger, Configuration config)
    {
        var lines = new List<string> { BuildSummaryLine(ledger) };
        lines.AddRange(BuildLootLines(ledger, config));

        if (ledger.LastDutyName.Length > 0)
            lines.Add("最后副本：" + ledger.LastDutyName);

        return lines;
    }

    /// <summary>按来源拆分（UI 用）。</summary>
    public static IEnumerable<string> BuildSourceBreakdown(SessionLedger ledger, Configuration config)
    {
        foreach (var it in EffectiveItems(config))
        {
            var total = ledger.GetCount(it.ItemId);
            if (total <= 0) continue;

            var bySrc = ledger.ItemBySource.GetValueOrDefault(it.ItemId);
            var m = bySrc?.GetValueOrDefault(Loot.LootSource.Monster) ?? 0;
            var c = bySrc?.GetValueOrDefault(Loot.LootSource.Chest) ?? 0;
            var r = bySrc?.GetValueOrDefault(Loot.LootSource.Roll) ?? 0;

            yield return $"[{Configuration.TierName(it.Tier)}] {it.Display}×{total}　小怪{m}/宝箱{c}/投掷{r}";
        }
    }
}
