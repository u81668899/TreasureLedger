using System;
using System.Collections.Generic;
using System.Linq;

namespace TreasureLedger.Ledger;

/// <summary>
/// 一次宝库行程里的一层。
///
/// 层数记录要点（按需求）：
/// - 进入宝库时先记一次 0 层（还没打开魔纹）
/// - 每打开一个传送魔纹 / 大门 → 层数 +1
/// - 运河的「X区」与宝库的「X层」是同一个东西，只是玩法不同
/// </summary>
public sealed class FloorRecord
{
    /// <summary>第几层，0 表示刚进本还没开魔纹。</summary>
    public int Floor { get; init; }

    /// <summary>进入这一层的时间。</summary>
    public DateTime EnteredAt { get; init; } = DateTime.Now;

    /// <summary>这一层是否已下底（遇到 boss）。</summary>
    public bool ReachedBottom { get; set; }

    /// <summary>下底 boss 名。</summary>
    public string BottomBoss { get; set; } = string.Empty;

    /// <summary>这一层打开了几个宝箱（卫月能直接看到，ACT 做不到）。</summary>
    public int ChestsOpened { get; set; }

    public override string ToString() => $"第{Floor}层{(ReachedBottom ? "（已下底）" : "")}";
}

/// <summary>
/// 一次完整的宝库行程。
/// </summary>
public sealed class VaultRun
{
    public string DungeonName { get; set; } = string.Empty;
    public uint TerritoryType { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }

    /// <summary>按顺序记录的层。</summary>
    public List<FloorRecord> Floors { get; } = new();

    /// <summary>最深到达的层。</summary>
    public int MaxFloor => Floors.Count == 0 ? 0 : Floors.Max(f => f.Floor);

    /// <summary>这趟是否到底过。</summary>
    public bool ReachedBottom => Floors.Any(f => f.ReachedBottom);

    public int BottomCount => Floors.Count(f => f.ReachedBottom);

    public int TotalChests => Floors.Sum(f => f.ChestsOpened);

    /// <summary>当前层记录（最后一条）。</summary>
    public FloorRecord? Current => Floors.Count == 0 ? null : Floors[^1];
}
