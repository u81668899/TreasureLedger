using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace TreasureLedger.Maps;

/// <summary>
/// 宝库场景探测：有没有箱子、箱子开完没、还有没有待投掷的东西。
///
/// 这是卫月比 ACT 强的地方 ——
/// ACT 看不到宝箱，退出时机只能靠金币数瞎猜；
/// 卫月能直接读 Treasure 对象和 Loot 列表。
/// </summary>
public sealed unsafe class VaultProbe
{
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;

    public VaultProbe(IPluginLog log, IObjectTable objectTable)
    {
        this.log = log;
        this.objectTable = objectTable;
    }

    /// <summary>场上还有几个未开的宝箱。</summary>
    public int CountUnopenedChests()
    {
        var count = 0;

        try
        {
            foreach (var obj in objectTable)
            {
                if (obj is null) continue;
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure) continue;

                var t = (Treasure*)obj.Address;
                if (t is null) continue;

                if (t->State == Treasure.TreasureState.Unopened
                    || t->State == Treasure.TreasureState.Opening)
                {
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug("[TreasureLedger] 扫描宝箱失败：{Msg}", ex.Message);
        }

        return count;
    }

    /// <summary>还有几件东西在等投掷。</summary>
    public int CountPendingRolls()
    {
        try
        {
            var loot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot.Instance();
            if (loot is null) return 0;

            var items = loot->Items;
            var pending = 0;

            for (var i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it.ItemId == 0) continue;

                var chestId = it.ChestObjectId;
                if (chestId is 0 or 3758096384u) continue;

                if (it.RollResult != 0) continue;

                var st = (int)it.RollState;
                if (st is 17 or 21 or 28) continue;

                pending++;
            }

            return pending;
        }
        catch
        {
            return 0;
        }
    }

}
