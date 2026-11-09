using System;
using System.Collections.Generic;

namespace TreasureLedger.Loot;

/// <summary>
/// 掉落去重：宝箱开启和投掷分配可能对同一件物品重复报两次。
/// 规则：同一物品在 <see cref="DedupWindow"/> 内，Roll 来源优先于 Chest；
/// 先到先得，后到的同源同物直接丢弃。
/// </summary>
public sealed class LootDeduplicator
{
    /// <summary>去重时间窗。宝箱与投掷通常在同一秒内出现，2 秒足够。</summary>
    public static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);

    private readonly Dictionary<uint, DateTime> lastSeen = new();

    /// <summary>返回 true 表示这条掉落应当被记录。</summary>
    public bool ShouldAccept(LootEntry entry, out string reason)
    {
        reason = string.Empty;

        if (entry.Count <= 0)
        {
            reason = "数量为0";
            return false;
        }

        if (lastSeen.TryGetValue(entry.ItemId, out var last))
        {
            var dt = entry.Time - last;
            if (dt >= TimeSpan.Zero && dt < DedupWindow)
            {
                reason = $"与 {dt.TotalMilliseconds:F0}ms 前的同物品重复";
                return false;
            }
        }

        lastSeen[entry.ItemId] = entry.Time;
        return true;
    }

    public void Reset() => lastSeen.Clear();
}
