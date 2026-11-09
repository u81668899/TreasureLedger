using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LootStruct = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot;

namespace TreasureLedger.Loot;

/// <summary>投掷选项。数值与游戏内部 RollResult 对齐。</summary>
public enum RollChoice : int
{
    Pass = 0,
    Need = 1,
    Greed = 2,

    /// <summary>游戏内部用 5 表示「已放弃/无需投掷」的结果位。</summary>
    AlreadyPassed = 5,
}

/// <summary>一条可投掷的条目。</summary>
public readonly record struct RollableItem(
    uint Index,
    uint ItemId,
    int Count,
    byte RollValue,
    RollState State,
    RollResult Result);

/// <summary>
/// 自动投掷。
///
/// 实现参考 LazyLoot（NiGuangOwO/LazyLoot，MIT）的反编译结果：
///   delegate bool RollItemRaw(Loot* loot, RollResult option, uint index);
///   特征码 "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08"
///   调用 RollItemRaw(Loot.Instance(), option, index)
///
/// 可投掷判定（照抄 LazyLoot 的过滤条件）：
///   ChestObjectId == 0 或 0xE0000000 → 不是宝箱来源，跳过
///   RollResult != 0                  → 已经投过或已分配，跳过
///   RollState 为 17 / 21 / 28        → 状态不可投，跳过
///   LootMode 为 2 / 3                → 模式排除，跳过
///   ItemId == 0                      → 空槽，跳过
/// </summary>
public sealed unsafe class AutoRoller : IDisposable
{
    private delegate bool RollItemRaw(LootStruct* loot, RollChoice option, uint index);

    private const string RollSignature = "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08";

    private readonly IPluginLog log;
    private readonly ISigScanner sigScanner;

    private RollItemRaw? rollItemRaw;

    /// <summary>上次投掷的时间，用于控制间隔。</summary>
    private DateTime lastRoll = DateTime.MinValue;

    /// <summary>投过但游戏还没反馈的物品，避免重复投。</summary>
    private readonly HashSet<(uint Index, uint ItemId)> issued = new();

    public AutoRoller(IPluginLog log, ISigScanner sigScanner)
    {
        this.log = log;
        this.sigScanner = sigScanner;
    }

    public void Dispose()
    {
        rollItemRaw = null;
        issued.Clear();
    }

    /// <summary>投掷函数是否已定位。</summary>
    public bool Ready => rollItemRaw is not null;

    /// <summary>累计投掷次数（统计用）。</summary>
    public int RolledCount { get; private set; }

    /// <summary>最近一次投掷描述。</summary>
    public string LastAction { get; private set; } = string.Empty;

    public bool TryInitialize()
    {
        if (rollItemRaw is not null) return true;

        try
        {
            var addr = sigScanner.ScanText(RollSignature);
            if (addr == IntPtr.Zero)
            {
                log.Warning("[TreasureLedger] 未找到投掷函数特征码");
                return false;
            }

            rollItemRaw = Marshal.GetDelegateForFunctionPointer<RollItemRaw>(addr);
            log.Information("[TreasureLedger] 投掷函数已定位 @ {Addr:X}", addr.ToInt64());
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 定位投掷函数失败");
            rollItemRaw = null;
            return false;
        }
    }

    /// <summary>枚举当前所有真正可投掷的条目。</summary>
    public List<RollableItem> EnumerateRollable()
    {
        var list = new List<RollableItem>(16);

        var loot = LootStruct.Instance();
        if (loot is null) return list;

        var items = loot->Items;
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];

            // 不是宝箱来源
            var chestId = it.ChestObjectId;
            if (chestId is 0 or 3758096384u) continue;

            // 已投过 / 已分配
            if (it.RollResult != 0) continue;

            // 状态不可投：17/21/28
            var state = (int)it.RollState;
            if (state is 17 or 21 or 28) continue;

            // 模式排除：2/3
            var mode = (int)it.LootMode;
            if (mode is 2 or 3) continue;

            // 空槽
            if (it.ItemId == 0) continue;

            list.Add(new RollableItem(
                (uint)i,
                it.ItemId,
                it.ItemCount,
                it.RollValue,
                it.RollState,
                it.RollResult));
        }

        return list;
    }

    /// <summary>
    /// 执行一轮自动投掷。
    /// 返回本次投出去的条数。
    /// </summary>
    /// <param name="config">投掷配置。</param>
    /// <param name="canRoll">外部条件（比如是否在宝库），返回 false 就不投。</param>
    public int RunOnce(AutoRollConfig config, Func<bool>? canRoll = null)
    {
        if (!config.Enabled) return 0;
        if (canRoll is not null && !canRoll()) return 0;

        // 间隔控制
        if ((DateTime.Now - lastRoll).TotalMilliseconds < config.IntervalMs) return 0;

        if (!TryInitialize()) return 0;

        var visited = 0;
        foreach (var item in EnumerateRollable())
        {
            // 已经投过、游戏还没反馈，跳过
            if (issued.Contains((item.Index, item.ItemId))) continue;

            var policy = config.ResolvePolicy(item.ItemId);
            var choice = policy switch
            {
                RollPolicy.Need => RollChoice.Need,
                RollPolicy.Greed => RollChoice.Greed,
                RollPolicy.Pass => RollChoice.Pass,
                _ => RollChoice.Greed,
            };

            if (DoRoll(item.Index, choice))
            {
                issued.Add((item.Index, item.ItemId));
                visited++;
                RolledCount++;
                LastAction = $"#{item.ItemId}×{item.Count} → {choice}";
                lastRoll = DateTime.Now;

                // 一次只投一个，让游戏有时间反应
                break;
            }
        }

        // 清掉已经不在可投列表里的记录，避免无限增长
        if (issued.Count > 64)
        {
            var still = new HashSet<(uint, uint)>();
            foreach (var r in EnumerateRollable())
                still.Add((r.Index, r.ItemId));
            issued.IntersectWith(still);
        }

        return visited;
    }

    /// <summary>底层投掷调用。</summary>
    private bool DoRoll(uint index, RollChoice choice)
    {
        if (!TryInitialize()) return false;

        try
        {
            var loot = LootStruct.Instance();
            if (loot is null) return false;

            rollItemRaw!(loot, choice, index);
            log.Debug("[TreasureLedger] 投掷 index={Index} choice={Choice}", index, choice);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 投掷失败 index={Index}", index);
            return false;
        }
    }

    /// <summary>清空记录（重置统计时用）。</summary>
    public void Reset()
    {
        issued.Clear();
        RolledCount = 0;
        LastAction = string.Empty;
    }
}
