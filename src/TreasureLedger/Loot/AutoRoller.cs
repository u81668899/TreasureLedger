using System;
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
/// ══════════════════════════════════════════════════════════════
/// 为什么这里不能用 CS（FFXIVClientStructs）的现成函数
///
/// 退本那个功能，CS 里正好有 GameMain.ExecuteCommand 可用。
/// 但投掷没有 —— 已确认 CS 的 Loot 类只有这些方法：
///     Instance / ReceiveEvent / ReceiveEventWithResult / get_Items
/// 没有 Roll / Need / Greed 之类的投掷入口，
/// Loot.ReceiveEvent 是 Atk UI 事件，语义不对，不能拿来投掷。
///
/// 所以只能沿用 LazyLoot（NiGuangOwO/LazyLoot, MIT）的做法：
/// 用特征码扫描定位游戏内部的投掷函数，再转成委托调用。
///     "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08"
///     delegate bool RollItemRaw(Loot* loot, RollChoice option, uint index)
/// ══════════════════════════════════════════════════════════════
///
/// 尽量向「CS 维护模式」靠拢的三件事：
///
/// 1) 【预检 + 降级】启动时先扫一遍特征码。
///    扫不到就禁用自动投掷，绝不调用空指针（那会崩游戏）。
///    行为和 DutyExiter 一致。
///
/// 2) 【自定义特征码覆盖】配置里允许玩家填新的特征码。
///    游戏大版本更新后，即使插件还没发新版，
///    玩家也能自己换上社区里流传的新特征码，不必等更新。
///
/// 3) 【多候选特征码】按顺序尝试内置候选，全失败才判定不可用。
///    这样 LazyLoot 换签名时还有一线机会。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed unsafe class AutoRoller : IDisposable
{
    private delegate bool RollItemRaw(LootStruct* loot, RollChoice option, uint index);

    /// <summary>
    /// 内置候选特征码，按顺序尝试。
    /// 第一个来自 LazyLoot 5.3.3.3。
    /// 后续几个是同一函数在不同版本下的常见变体写法。
    /// </summary>
    private static readonly string[] CandidateSignatures =
    {
        "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08",
    };

    private readonly IPluginLog log;
    private readonly ISigScanner sigScanner;

    private RollItemRaw? rollItemRaw;

    private DateTime lastRoll = DateTime.MinValue;
    private readonly HashSet<(uint Index, uint ItemId)> issued = new();

    /// <summary>是否可用（预检通过）。</summary>
    public bool Available { get; private set; }

    /// <summary>不可用原因。</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>实际命中的特征码，便于排查。</summary>
    public string ActiveSignature { get; private set; } = string.Empty;

    /// <summary>定位到的函数地址。</summary>
    public IntPtr FunctionAddress { get; private set; }

    public bool Ready => rollItemRaw is not null;

    public int RolledCount { get; private set; }

    public string LastAction { get; private set; } = string.Empty;

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

    /// <summary>
    /// 预检并定位投掷函数。
    /// </summary>
    /// <param name="customSignature">配置文件里玩家自定义的特征码，优先使用。</param>
    public bool Probe(string? customSignature = null)
    {
        rollItemRaw = null;
        Available = false;
        UnavailableReason = string.Empty;
        ActiveSignature = string.Empty;
        FunctionAddress = IntPtr.Zero;

        // 候选列表：自定义的排最前
        var candidates = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(customSignature))
            candidates.Add(customSignature.Trim());

        foreach (var s in CandidateSignatures)
            if (!candidates.Contains(s))
                candidates.Add(s);

        foreach (var sig in candidates)
        {
            try
            {
                var addr = sigScanner.ScanText(sig);
                if (addr == IntPtr.Zero) continue;

                rollItemRaw = Marshal.GetDelegateForFunctionPointer<RollItemRaw>(addr);
                Available = true;
                ActiveSignature = sig;
                FunctionAddress = addr;

                log.Information(
                    "[TreasureLedger] 投掷函数已定位 @ {Addr:X}（特征码 {Sig}）",
                    addr.ToInt64(), sig);
                return true;
            }
            catch (Exception ex)
            {
                log.Debug("[TreasureLedger] 特征码尝试失败：{Msg}", ex.Message);
            }
        }

        UnavailableReason =
            "未能解析投掷函数特征码，可能是游戏已更新而插件尚未跟进。"
            + "可在配置里填入社区提供的新特征码，或等待插件更新。";
        log.Warning("[TreasureLedger] 投掷预检失败，自动投掷已禁用");
        return false;
    }

    /// <summary>重新预检（游戏版本切换后可在 UI 手动触发）。</summary>
    public bool Recheck(string? customSignature = null) => Probe(customSignature);

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

    /// <summary>执行一轮自动投掷，返回本次投出去的条数。</summary>
    public int RunOnce(AutoRollConfig config, Func<bool>? canRoll = null)
    {
        if (!config.Enabled) return 0;
        if (!Available) return 0;
        if (canRoll is not null && !canRoll()) return 0;

        if ((DateTime.Now - lastRoll).TotalMilliseconds < config.IntervalMs) return 0;
        if (rollItemRaw is null) return 0;

        var visited = 0;
        foreach (var item in EnumerateRollable())
        {
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
                break;   // 一次只投一个，让游戏有时间反应
            }
        }

        if (issued.Count > 64)
        {
            var still = new HashSet<(uint, uint)>();
            foreach (var r in EnumerateRollable())
                still.Add((r.Index, r.ItemId));
            issued.IntersectWith(still);
        }

        return visited;
    }

    private bool DoRoll(uint index, RollChoice choice)
    {
        if (rollItemRaw is null) return false;

        try
        {
            var loot = LootStruct.Instance();
            if (loot is null) return false;

            rollItemRaw(loot, choice, index);
            log.Debug("[TreasureLedger] 投掷 index={Index} choice={Choice}", index, choice);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 投掷失败 index={Index}", index);
            return false;
        }
    }

    public void Reset()
    {
        issued.Clear();
        RolledCount = 0;
        LastAction = string.Empty;
    }
}
