using System;
using System.Collections.Generic;

namespace TreasureLedger.Storage;

/// <summary>
/// 一次完整挖宝行程的持久化记录。
/// 这是长期存储的最小单位，分卷文件里存的就是一个 <see cref="RunRecord"/> 列表。
/// </summary>
public sealed class RunRecord
{
    /// <summary>唯一 id，用于去重与定位。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>开始时间（本地时间）。</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>副本名。</summary>
    public string DungeonName { get; set; } = string.Empty;

    /// <summary>TerritoryType。</summary>
    public uint TerritoryType { get; set; }

    /// <summary>最深层。</summary>
    public int MaxFloor { get; set; }

    /// <summary>下底次数。</summary>
    public int BottomCount { get; set; }

    /// <summary>开箱数。</summary>
    public int ChestsOpened { get; set; }

    /// <summary>本趟获得的金币。</summary>
    public long Gil { get; set; }

    /// <summary>本趟掉落：ItemId → 数量。</summary>
    public Dictionary<uint, int> Items { get; set; } = new();

    /// <summary>本趟掉落来源拆分：ItemId → 来源 Count。</summary>
    public Dictionary<uint, SourceCounts> Sources { get; set; } = new();

    /// <summary>神殿分级计数。</summary>
    public Dictionary<string, int> ShrineTiers { get; set; } = new();

    /// <summary>耗时。</summary>
    public TimeSpan Duration => (EndedAt ?? DateTime.Now) - StartedAt;

    public override string ToString()
        => $"{StartedAt:MM-dd HH:mm} {DungeonName} 深层{MaxFloor} 底{BottomCount} 箱{ChestsOpened} {Gil:n0}金";
}

/// <summary>一件物品按来源的计数。</summary>
public sealed class SourceCounts
{
    public int Monster { get; set; }
    public int Chest { get; set; }
    public int Roll { get; set; }

    public int Total => Monster + Chest + Roll;
}
