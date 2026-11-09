using System;
using System.Collections.Generic;

namespace TreasureLedger.Loot;

/// <summary>
/// 投掷与进包的去重。
///
/// 背景（实测）：
/// - 1254「获得了新的战利品」= 投掷通知，物品可能还在投掷阶段，很久才进包
/// - 1232/1233「获得了"XX"」= 实际进包
///
/// 难点：同一种物品可能同时有多件在投掷，也同时有多件从小怪/宝箱掉落。
/// 所以不能按「物品 ID 单一登记」处理，必须按件数排队。
///
/// 规则：
/// - 收到 1254，该物品的待兑现队列 +N（不立刻计入统计）
/// - 收到 1232/1233 进包，若该物品有待兑现条目：
///     消耗，来源记为 Roll；剩余条目继续等待
/// - 队列为空时，按普通掉落（小怪/宝箱）处理
/// - 每条记录带时间戳，超期作废，避免僵尸计数
/// </summary>
public sealed class LootDeduplicator
{
    /// <summary>投掷登记有效期。超过作废。</summary>
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(30);

    /// <summary>一条待兑现的投掷记录（对应一件物品）。</summary>
    private sealed class Pending
    {
        public DateTime SeenAt;
    }

    /// <summary>物品 ID → 待兑现的投掷件（FIFO）。</summary>
    private readonly Dictionary<uint, Queue<Pending>> pendingRoll = new();

    /// <summary>投掷通知：登记 count 件待进包。</summary>
    public void RegisterRoll(uint itemId, int count)
    {
        if (itemId == 0 || count <= 0) return;

        if (!pendingRoll.TryGetValue(itemId, out var q))
        {
            q = new Queue<Pending>();
            pendingRoll[itemId] = q;
        }

        var now = DateTime.Now;
        for (var i = 0; i < count; i++)
            q.Enqueue(new Pending { SeenAt = now });
    }

    /// <summary>
    /// 处理一条进包掉落，决定来源与数量。
    /// 返回值：应计入的 (来源, 数量)，可能拆成两段。
    /// </summary>
    public ResolvedLoot Resolve(uint itemId, int count, LootSource defaultSource)
    {
        if (count <= 0) return new ResolvedLoot(defaultSource, 0, 0);

        if (!pendingRoll.TryGetValue(itemId, out var q) || q.Count == 0)
            return new ResolvedLoot(defaultSource, count, 0);

        // 先清掉过期的
        PruneQueue(q);
        if (q.Count == 0)
        {
            pendingRoll.Remove(itemId);
            return new ResolvedLoot(defaultSource, count, 0);
        }

        var fromRoll = 0;
        while (fromRoll < count && q.Count > 0)
        {
            q.Dequeue();
            fromRoll++;
        }

        if (q.Count == 0) pendingRoll.Remove(itemId);

        var fromNormal = count - fromRoll;
        return new ResolvedLoot(LootSource.Roll, fromRoll, fromNormal, defaultSource);
    }

    private static void PruneQueue(Queue<Pending> q)
    {
        var now = DateTime.Now;
        while (q.Count > 0 && now - q.Peek().SeenAt > PendingTimeout)
            q.Dequeue();
    }

    /// <summary>该物品还有多少件待兑现。</summary>
    public int PendingFor(uint itemId)
        => pendingRoll.TryGetValue(itemId, out var q) ? q.Count : 0;

    /// <summary>清理所有过期登记。</summary>
    public void Prune()
    {
        var empty = new List<uint>();
        foreach (var kv in pendingRoll)
        {
            PruneQueue(kv.Value);
            if (kv.Value.Count == 0) empty.Add(kv.Key);
        }
        foreach (var id in empty) pendingRoll.Remove(id);
    }

    public int TrackedItemKinds => pendingRoll.Count;

    public int TotalPending()
    {
        var n = 0;
        foreach (var q in pendingRoll.Values) n += q.Count;
        return n;
    }

    public void Reset() => pendingRoll.Clear();
}

/// <summary>去重结果：投掷部分与普通部分分别计数。</summary>
public readonly struct ResolvedLoot
{
    public ResolvedLoot(LootSource source, int count, int leftover, LootSource leftoverSource = LootSource.Monster)
    {
        Source = source;
        Count = count;
        Leftover = leftover;
        LeftoverSource = leftoverSource;
    }

    /// <summary>主要来源。</summary>
    public LootSource Source { get; }

    /// <summary>该来源下的数量。</summary>
    public int Count { get; }

    /// <summary>未被投掷消化、按原来源计的剩余数量。</summary>
    public int Leftover { get; }

    public LootSource LeftoverSource { get; }

    public bool HasLeftover => Leftover > 0;
}
