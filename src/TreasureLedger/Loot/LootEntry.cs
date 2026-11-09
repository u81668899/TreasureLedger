using System;

namespace TreasureLedger.Loot;

/// <summary>掉落的来源分类，用于去重与分项统计。</summary>
public enum LootSource
{
    /// <summary>小怪掉落（1232/1233）</summary>
    Monster,

    /// <summary>宝箱开启（1233，进入宝库后开箱）</summary>
    Chest,

    /// <summary>投掷分配（1254）</summary>
    Roll,
}

/// <summary>一条掉落记录。</summary>
public sealed class LootEntry
{
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Count { get; init; }
    public LootSource Source { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;

    public override string ToString() => $"{ItemName}×{Count}({Source})";
}
