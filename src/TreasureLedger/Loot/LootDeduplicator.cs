using System;
using System.Collections.Generic;

namespace TreasureLedger.Loot;

/// <summary>
/// 投掷与获取的去重。
///
/// 规则（按需求）：
/// - 1254「获得了新的战利品」= 投掷成功通知，物品可能还在投掷阶段、很久才进包
/// - 1232/1233「获得了"XX"」= 实际进包
/// 同一物品可能先报 1254，几秒到几分钟后才报 1232。
/// 因此不用时间窗，改用「物品 ID 投掷登记」：
///   收到 1254 → 登记该物品处于投掷待入包状态，不计数
///   收到 1232/1233 → 若该物品在待入包登记里，则消费登记，标记来源为 Roll；否则按普通掉落处理
/// </summary>
public sealed class LootDeduplicator
{
    /// <summary>登记多久没兑现就作废，防止无限累积。</summary>
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(30);

    private readonly Dictionary<uint, DateTime> pendingRoll = new();

    /// <summary>
    /// 投掷成功通知。登记为待入包，不计数。
    /// </summary>
    public void RegisterRollPending(uint itemId)
    {
        if (itemId == 0) return;
        pendingRoll[itemId] = DateTime.Now;
    }

    /// <summary>
    /// 判断这条进包掉落是否来自刚才的投掷。
    /// 返回 true 表示消费了投掷登记，来源应记为 Roll。
    /// </summary>
    public bool TryConsumeRollPending(uint itemId)
    {
        if (!pendingRoll.TryGetValue(itemId, out var t)) return false;

        if (DateTime.Now - t > PendingTimeout)
        {
            pendingRoll.Remove(itemId);
            return false;
        }

        pendingRoll.Remove(itemId);
        return true;
    }

    /// <summary>清理过期的待入包登记。</summary>
    public void Prune()
    {
        var now = DateTime.Now;
        var expired = new List<uint>();
        foreach (var kv in pendingRoll)
            if (now - kv.Value > PendingTimeout)
                expired.Add(kv.Key);

        foreach (var id in expired) pendingRoll.Remove(id);
    }

    public int PendingCount => pendingRoll.Count;

    public void Reset() => pendingRoll.Clear();
}
