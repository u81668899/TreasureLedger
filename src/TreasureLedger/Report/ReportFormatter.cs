using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreasureLedger.Items;
using TreasureLedger.Ledger;

namespace TreasureLedger.Report;

/// <summary>
/// 播报格式，复刻 Triggernometry XML 里的输出。
/// </summary>
public static class ReportFormatter
{
    /// <summary>
    /// 「本次掉落内容：单人绿×1　深层绿×2　…」
    /// 只列已配置且数量大于 0 的物品，顺序按 TrackedItems。
    /// </summary>
    public static string BuildLootLine(SessionLedger ledger, IReadOnlySet<uint>? disabled = null)
    {
        var sb = new StringBuilder();
        foreach (var it in TrackedItems.All)
        {
            if (disabled is not null && disabled.Contains(it.ItemId)) continue;

            var n = ledger.GetCount(it.ItemId);
            if (n <= 0) continue;

            sb.Append(it.Display).Append('×').Append(n).Append('　');
        }

        var body = sb.ToString().TrimEnd('　');
        return body.Length == 0
            ? "似乎还没有值钱的掉落物…"
            : "本次掉落内容：" + body;
    }

    /// <summary>「金币：x　图数：x　洞数：x　底数：x」</summary>
    public static string BuildSummaryLine(SessionLedger ledger)
        => $"金币：{ledger.Gil:n0}　图数：{ledger.MapCount}　洞数：{ledger.VaultCount}　底数：{ledger.BottomCount}";

    /// <summary>按来源拆分的详细统计（调试/UI 用）。</summary>
    public static IEnumerable<string> BuildSourceBreakdown(SessionLedger ledger)
    {
        foreach (var it in TrackedItems.All)
        {
            var total = ledger.GetCount(it.ItemId);
            if (total <= 0) continue;

            var bySrc = ledger.ItemBySource.GetValueOrDefault(it.ItemId);
            var m = bySrc?.GetValueOrDefault(Loot.LootSource.Monster) ?? 0;
            var c = bySrc?.GetValueOrDefault(Loot.LootSource.Chest) ?? 0;
            var r = bySrc?.GetValueOrDefault(Loot.LootSource.Roll) ?? 0;

            yield return $"{it.Display}×{total} (小怪{m}/宝箱{c}/投掷{r})";
        }
    }

    /// <summary>完整统计（多行）。</summary>
    public static IReadOnlyList<string> BuildFullReport(SessionLedger ledger, IReadOnlySet<uint>? disabled = null)
    {
        var lines = new List<string> { BuildSummaryLine(ledger) };

        var loot = BuildLootLine(ledger, disabled);
        lines.Add(loot);

        if (ledger.LastDutyName.Length > 0)
            lines.Add("最后副本：" + ledger.LastDutyName);

        return lines;
    }
}
