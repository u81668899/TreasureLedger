using System;
using System.Collections.Generic;
using System.Linq;
using TreasureLedger.Maps;

namespace TreasureLedger.Ledger;

/// <summary>神殿召唤的分级。G10/G12/G15 用上中下级，G17-G18 用大梦终梦。</summary>
public enum ShrineTier
{
    /// <summary>上级（G17-G18 里叫「大梦」）</summary>
    High,

    /// <summary>中级</summary>
    Middle,

    /// <summary>下级（G17-G18 里叫「终梦」）</summary>
    Low,
}

/// <summary>本次会话的统计数据。</summary>
public sealed class SessionLedger
{
    // ── 掉落 ──
    public Dictionary<uint, int> ItemCounts { get; } = new();
    public Dictionary<uint, Dictionary<Loot.LootSource, int>> ItemBySource { get; } = new();

    // ── 计数 ──
    /// <summary>金币累计。</summary>
    public long Gil { get; set; }

    /// <summary>地图数：进入宝库（任务开始）+ 陷阱魔物全清。</summary>
    public int MapCount { get; set; }

    /// <summary>洞数：每次进入宝库（开洞）。</summary>
    public int VaultCount { get; set; }

    /// <summary>底数：累计下底次数。</summary>
    public int BottomCount { get; set; }

    // ── 神殿分级 ──
    /// <summary>神殿召唤次数，按分级。</summary>
    public Dictionary<ShrineTier, int> ShrineTiers { get; } = new();

    // ── 本次行程 ──
    /// <summary>当前正在进行的宝库行程。</summary>
    public VaultRun? CurrentRun { get; set; }

    /// <summary>已结束的行程（供 UI 查看）。</summary>
    public List<VaultRun> FinishedRuns { get; } = new();

    public string LastDutyName { get; set; } = string.Empty;
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

    // ── 神殿分级 ──
    public void AddShrineTier(ShrineTier tier)
    {
        ShrineTiers[tier] = ShrineTiers.GetValueOrDefault(tier) + 1;
    }

    public int GetShrineTier(ShrineTier tier) => ShrineTiers.GetValueOrDefault(tier);

    /// <summary>当前宝图是否用大梦/终梦命名。</summary>
    public bool UsesDreamNaming => TreasureDungeons.UsesDreamTiers(
        CurrentRun is null ? null : TreasureDungeons.Find(CurrentRun.TerritoryType));

    // ── 行程 ──
    /// <summary>开始新的宝库行程。进入本时先补一条 0 层。</summary>
    public VaultRun StartRun(string dungeonName, uint territoryType)
    {
        if (CurrentRun is not null)
        {
            CurrentRun.EndedAt = DateTime.Now;
            FinishedRuns.Add(CurrentRun);
        }

        var run = new VaultRun
        {
            DungeonName = dungeonName,
            TerritoryType = territoryType,
            StartedAt = DateTime.Now,
        };

        // 进入本先记 0 层
        run.Floors.Add(new FloorRecord { Floor = 0 });

        CurrentRun = run;
        LastDutyName = dungeonName;
        return run;
    }

    /// <summary>推进到下一层。</summary>
    public FloorRecord? AdvanceFloor()
    {
        var run = CurrentRun;
        if (run is null) return null;

        var next = (run.Current?.Floor ?? 0) + 1;
        var rec = new FloorRecord { Floor = next };
        run.Floors.Add(rec);
        return rec;
    }

    /// <summary>标记当前层已下底。</summary>
    public void MarkBottom(string bossName)
    {
        var run = CurrentRun;
        var cur = run?.Current;
        if (cur is null) return;

        if (cur.ReachedBottom) return;
        cur.ReachedBottom = true;
        cur.BottomBoss = bossName;
        BottomCount++;
    }

    /// <summary>当前层开了一个宝箱。</summary>
    public void AddChest()
    {
        var cur = CurrentRun?.Current;
        if (cur is not null) cur.ChestsOpened++;
    }

    /// <summary>结束当前行程。</summary>
    public VaultRun? EndRun()
    {
        var run = CurrentRun;
        if (run is null) return null;

        run.EndedAt = DateTime.Now;
        FinishedRuns.Add(run);
        CurrentRun = null;
        return run;
    }

    public void Reset()
    {
        ItemCounts.Clear();
        ItemBySource.Clear();
        ShrineTiers.Clear();
        FinishedRuns.Clear();
        CurrentRun = null;

        Gil = 0;
        MapCount = 0;
        VaultCount = 0;
        BottomCount = 0;
        LastDutyName = string.Empty;
        StartedAt = DateTime.Now;
    }
}
