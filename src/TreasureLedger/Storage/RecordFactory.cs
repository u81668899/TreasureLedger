using System;
using System.Collections.Generic;
using System.Linq;
using TreasureLedger.Ledger;
using TreasureLedger.Storage;

namespace TreasureLedger.Storage;

/// <summary>
/// 把内存里的行程数据转成可持久化的记录。
/// </summary>
public static class RecordFactory
{
    /// <summary>从 VaultRun 生成一条持久化记录。</summary>
    public static RunRecord FromRun(VaultRun run, RunGains gains)
    {
        var rec = new RunRecord
        {
            StartedAt = run.StartedAt,
            EndedAt = run.EndedAt ?? DateTime.Now,
            DungeonName = run.DungeonName,
            TerritoryType = run.TerritoryType,
            MaxFloor = run.MaxFloor,
            BottomCount = run.BottomCount,
            ChestsOpened = run.TotalChests,
            Gil = gains.Gil,
            Items = new Dictionary<uint, int>(gains.Items),
            Sources = gains.Sources.ToDictionary(
                kv => kv.Key,
                kv => new SourceCounts
                {
                    Monster = kv.Value.GetValueOrDefault(Loot.LootSource.Monster),
                    Chest = kv.Value.GetValueOrDefault(Loot.LootSource.Chest),
                    Roll = kv.Value.GetValueOrDefault(Loot.LootSource.Roll),
                }),
            ShrineTiers = gains.ShrineTiers.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value),
        };

        return rec;
    }
}

/// <summary>
/// 一趟行程内累积的收益。行程结束时快照一次，然后清零，避免跨行程串数据。
/// </summary>
public sealed class RunGains
{
    public long Gil { get; set; }
    public Dictionary<uint, int> Items { get; } = new();
    public Dictionary<uint, Dictionary<Loot.LootSource, int>> Sources { get; } = new();
    public Dictionary<ShrineTier, int> ShrineTiers { get; } = new();

    public void AddItem(uint itemId, int count, Loot.LootSource source)
    {
        if (count <= 0) return;

        Items[itemId] = Items.GetValueOrDefault(itemId) + count;

        if (!Sources.TryGetValue(itemId, out var s))
        {
            s = new Dictionary<Loot.LootSource, int>();
            Sources[itemId] = s;
        }
        s[source] = s.GetValueOrDefault(source) + count;
    }

    public void AddShrineTier(ShrineTier tier)
        => ShrineTiers[tier] = ShrineTiers.GetValueOrDefault(tier) + 1;

    public bool IsEmpty => Items.Count == 0 && Gil == 0 && ShrineTiers.Count == 0;

    public void Clear()
    {
        Gil = 0;
        Items.Clear();
        Sources.Clear();
        ShrineTiers.Clear();
    }
}
