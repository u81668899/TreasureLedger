using System;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Maps;

/// <summary>自动退出的当前状态。</summary>
public enum AutoExitState
{
    /// <summary>未触发</summary>
    Idle,

    /// <summary>条件已满足，正在等待</summary>
    Waiting,

    /// <summary>已发送退出</summary>
    Exited,
}

/// <summary>
/// 下底自动退出。
///
/// 退出条件（三者都满足）：
/// 1. 这一趟已经下底过（可关）
/// 2. 场上没有未开的宝箱
/// 3. 没有待投掷的物品（可关）
///
/// 满足后再等 N 秒缓冲，避免刚打完还没刷出箱子就退。
///
/// 为什么卫月做得比 ACT 准：
/// ACT 看不到宝箱，只能靠金币数变化来猜，
/// 卫月能直接读 Treasure 对象和 Loot 列表。
/// </summary>
public sealed class AutoExiter
{
    private readonly IPluginLog log;
    private readonly VaultProbe probe;
    private readonly Action<string> notify;

    private DateTime conditionMetAt = DateTime.MinValue;

    public AutoExitState State { get; private set; } = AutoExitState.Idle;

    /// <summary>最近一次为什么等待（调试用）。</summary>
    public string LastReason { get; private set; } = string.Empty;

    public AutoExiter(IPluginLog log, VaultProbe probe, Action<string> notify)
    {
        this.log = log;
        this.probe = probe;
        this.notify = notify;
    }

    /// <summary>重置状态（进新本或手动重置时）。</summary>
    public void Reset()
    {
        State = AutoExitState.Idle;
        conditionMetAt = DateTime.MinValue;
        LastReason = string.Empty;
    }

    /// <summary>
    /// 每帧调用。返回 true 表示这一帧应该执行退出。
    /// </summary>
    /// <param name="cfg">配置。</param>
    /// <param name="inDungeon">是否在宝库副本里。</param>
    /// <param name="hasBottom">这一趟是否下底过。</param>
    public bool Tick(Loot.AutoExitConfig cfg, bool inDungeon, bool hasBottom)
    {
        if (!cfg.Enabled)
        {
            if (State != AutoExitState.Idle) Reset();
            return false;
        }

        // 不在本里 / 还没下底 → 不触发
        if (!inDungeon)
        {
            Reset();
            return false;
        }

        if (cfg.RequireBottom && !hasBottom)
        {
            LastReason = "尚未下底";
            return false;
        }

        // 已经退出了就别重复
        if (State == AutoExitState.Exited) return false;

        var chests = probe.CountUnopenedChests();
        if (chests > 0)
        {
            LastReason = $"还有 {chests} 个箱子没开";
            conditionMetAt = DateTime.MinValue;
            State = AutoExitState.Idle;
            return false;
        }

        if (cfg.RequireRollsFinished)
        {
            var pending = probe.CountPendingRolls();
            if (pending > 0)
            {
                LastReason = $"还有 {pending} 件待投掷";
                conditionMetAt = DateTime.MinValue;
                State = AutoExitState.Idle;
                return false;
            }
        }

        // 条件满足，开始计时
        if (conditionMetAt == DateTime.MinValue)
        {
            conditionMetAt = DateTime.Now;
            State = AutoExitState.Waiting;
            LastReason = $"条件已满足，{cfg.WaitSeconds} 秒后退出";
            log.Information("[TreasureLedger] 自动退出条件满足，等待 {S} 秒", cfg.WaitSeconds);

            if (cfg.NotifyBeforeExit)
                notify($"宝库已完成，{cfg.WaitSeconds} 秒后自动退出");
        }

        // 等待期间再确认一次，避免这期间又刷出箱子
        if (chests > 0 || (cfg.RequireRollsFinished && probe.CountPendingRolls() > 0))
        {
            conditionMetAt = DateTime.MinValue;
            State = AutoExitState.Idle;
            LastReason = "等待期间出现新目标，取消退出";
            return false;
        }

        if ((DateTime.Now - conditionMetAt).TotalSeconds < cfg.WaitSeconds)
            return false;

        // 执行退出
        State = AutoExitState.Exited;
        LastReason = "已退出";
        log.Information("[TreasureLedger] 自动退出");

        if (cfg.NotifyBeforeExit)
            notify("宝库已完成，正在退出");

        return true;
    }
}
