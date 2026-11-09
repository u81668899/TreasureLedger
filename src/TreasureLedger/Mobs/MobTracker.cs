using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Mobs;

/// <summary>
/// 小怪追踪器。
///
/// 原理：
///   ACT 靠日志里的 AddCombatant / Death / RemoveCombatant 事件。
///   卫月没有这层日志，但能直接读 ObjectTable。
///   所以定期扫描 ObjectTable，对比前后差异：
///     - 新出现的匹配名单的对象 → 记为「出现」
///     - 消失的对象 → 看它消失前是否已死
///         已死  → 成功
///         未死  → 失败（逃走/被清掉）
///
/// 为什么需要缓存死亡状态：
///   ObjectTable 里的对象一旦移除就查不到了，
///   所以每次扫描都要把 IsDead 记下来，消失时用最后一次的值判定。
///
/// 扫描频率限流到 150ms，避免每帧遍历对象表造成卡顿。
/// </summary>
public sealed class MobTracker : IDisposable
{
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;

    /// <summary>扫描间隔。</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(150);

    private DateTime lastScan = DateTime.MinValue;

    /// <summary>正在追踪的对象：EntityId → 追踪状态。</summary>
    private readonly Dictionary<uint, Tracked> tracking = new();

    /// <summary>小怪统计。</summary>
    public MobRecord Record { get; } = new();

    /// <summary>某个小怪出现时触发，用于打标记。</summary>
    public event Action<MobDefinition, ulong>? MobAppeared;

    /// <summary>跟踪状态。</summary>
    private sealed class Tracked
    {
        public string Name = string.Empty;
        public MobDefinition? Def;
        public bool LastDead;
        public DateTime FirstSeen = DateTime.Now;
    }

    public MobTracker(IPluginLog log, IObjectTable objectTable)
    {
        this.log = log;
        this.objectTable = objectTable;
    }

    public void Dispose() => tracking.Clear();

    public void Reset()
    {
        tracking.Clear();
        Record.Clear();
    }

    /// <summary>每帧调用，内部限流。</summary>
    public void Tick()
    {
        if (DateTime.Now - lastScan < ScanInterval) return;
        lastScan = DateTime.Now;

        try { Scan(); }
        catch (Exception ex) { log.Debug("[TreasureLedger] 小怪扫描异常：{Msg}", ex.Message); }
    }

    private void Scan()
    {
        var seen = new HashSet<uint>();

        foreach (var obj in objectTable)
        {
            if (obj is null) continue;

            // 只关心战斗 NPC
            if (obj.ObjectKind != ObjectKind.BattleNpc) continue;

            string name;
            try { name = obj.Name.TextValue ?? string.Empty; }
            catch { continue; }

            if (name.Length == 0) continue;

            var def = MobCatalog.Find(name);
            if (def is null) continue;

            var id = obj.EntityId;
            seen.Add(id);

            ulong gameObjectId = 0;
            try { gameObjectId = obj.GameObjectId; } catch { }

            if (!tracking.TryGetValue(id, out var t))
            {
                // 新出现
                t = new Tracked { Name = name, Def = def };
                tracking[id] = t;
                log.Debug("[TreasureLedger] 小怪出现：{Name}（{Cat}）", name, def.Category);
                MobAppeared?.Invoke(def, gameObjectId);
            }

            // 更新死亡状态
            try { t.LastDead = obj.IsDead; } catch { }
        }

        // 处理消失的对象
        if (tracking.Count == 0) return;

        var gone = new List<uint>();
        foreach (var kv in tracking)
            if (!seen.Contains(kv.Key))
                gone.Add(kv.Key);

        foreach (var id in gone)
        {
            var t = tracking[id];
            tracking.Remove(id);

            if (t.Def is null) continue;

            // 消失前已死 → 成功；否则逃走 → 失败
            var success = t.LastDead;
            Record.Add(t.Def.Category, success);

            log.Debug("[TreasureLedger] 小怪{Result}：{Name}",
                success ? "击杀" : "逃走", t.Name);
        }
    }

    /// <summary>当前正在追踪的数量。</summary>
    public int ActiveCount => tracking.Count;
}
