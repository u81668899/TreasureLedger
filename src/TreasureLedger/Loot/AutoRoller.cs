using System;
using System.Linq;
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
}

/// <summary>
/// 自动投掷。
///
/// 实现参考 LazyLoot（NiGuangOwO/LazyLoot，MIT）：
///   delegate bool RollItemRaw(Loot* loot, RollResult option, uint index);
///   通过特征码扫描定位游戏内的投掷函数：
///     "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08"
///   然后 RollItemRaw(Loot.Instance(), option, index)。
///
/// LootItem 字段（CS 7.57）：
///   ChestObjectId off=0, ItemId off=8, ItemCount off=12,
///   RollState off=32, RollResult off=36, RollValue off=40, LootMode off=56
/// </summary>
public sealed unsafe class AutoRoller : IDisposable
{
    private delegate bool RollItemRaw(LootStruct* loot, RollChoice option, uint index);

    private const string RollSignature = "41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08";

    private readonly IPluginLog log;
    private readonly ISigScanner sigScanner;

    private RollItemRaw? rollItemRaw;

    public AutoRoller(IPluginLog log, ISigScanner sigScanner)
    {
        this.log = log;
        this.sigScanner = sigScanner;
    }

    public void Dispose() => rollItemRaw = null;

    /// <summary>投掷函数是否已定位成功。</summary>
    public bool Ready => rollItemRaw is not null;

    /// <summary>尝试定位投掷函数。失败不抛异常。</summary>
    public bool TryInitialize()
    {
        if (rollItemRaw is not null) return true;

        try
        {
            var addr = sigScanner.ScanText(RollSignature);
            if (addr == IntPtr.Zero)
            {
                log.Warning("[TreasureLedger] 未找到投掷函数特征码，自动投掷不可用");
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

    /// <summary>枚举当前可投掷的物品。</summary>
    public List<(uint Index, uint ItemId, int Count, RollState State, RollResult Result)> EnumerateRollable()
    {
        var list = new List<(uint, uint, int, RollState, RollResult)>();

        var loot = LootStruct.Instance();
        if (loot is null) return list;

        var items = loot->Items;
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it.ItemId == 0) continue;

            // ChestObjectId 为 0 或 0xE0000000 表示不是宝箱来源
            var chestId = it.ChestObjectId;
            if (chestId is 0 or 3758096384u) continue;

            list.Add(((uint)i, it.ItemId, (int)it.ItemCount, it.RollState, it.RollResult));
        }

        return list;
    }

    /// <summary>
    /// 对指定索引投掷。
    /// </summary>
    public bool Roll(uint index, RollChoice choice)
    {
        if (!TryInitialize()) return false;

        try
        {
            var loot = LootStruct.Instance();
            if (loot is null) return false;

            rollItemRaw!(loot, choice, index);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 投掷失败 index={Index}", index);
            return false;
        }
    }
}
