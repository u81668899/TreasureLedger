using System;
using System.Collections.Generic;
using System.Linq;

namespace TreasureLedger.Ledger;

/// <summary>本次会话的统计数据。</summary>
public sealed class SessionLedger
{
    /// <summary>物品 ID → 累计数量</summary>
    public Dictionary<uint, int> ItemCounts { get; } = new();

    /// <summary>物品 ID → 按来源拆分</summary>
    public Dictionary<uint, Dictionary<Loot.LootSource, int>> ItemBySource { get; } = new();

    public long Gil { get; set; }
    public int MapCount { get; set; }
    public int VaultCount { get; set; }
    public int BottomCount { get; set; }

    /// <summary>最近一次进入的副本名。</summary>
    public string LastDutyName { get; set; } = string.Empty;

    /// <summary>本次会话开始时间。</summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    public void AddItem(uint itemId, int count, Loot.LootSource source)
    {
        if (count <= 0) return;

        ItemCounts[itemId] = ItemCounts.GetValueOrDefault(itemId) + count;

        if (!ItemBySource.TryGetValue(itemId, out var bySrc))
        {
            bySrc = new Dictionary<Loot.LootSource, int>();
            ItemBySource[itemId] = bySrc;
        }
        bySrc[source] = bySrc.GetValueOrDefault(source) + count;
    }

    public int GetCount(uint itemId) => ItemCounts.GetValueOrDefault(itemId);

    public int TotalItems => ItemCounts.Values.Sum();

    public void Reset()
    {
        ItemCounts.Clear();
        ItemBySource.Clear();
        Gil = 0;
        MapCount = 0;
        VaultCount = 0;
        BottomCount = 0;
        LastDutyName = string.Empty;
        StartedAt = DateTime.Now;
    }
}
