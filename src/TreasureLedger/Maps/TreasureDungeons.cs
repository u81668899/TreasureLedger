using System;
using System.Collections.Generic;

namespace TreasureLedger.Maps;

/// <summary>
/// 宝库副本定义。TerritoryType 沿用 ACT/Trig 里已验证可用的编号。
/// </summary>
public sealed record TreasureDungeon(
    string Name,
    DungeonFamily Family,
    IReadOnlyList<uint> TerritoryTypes);

public enum DungeonFamily
{
    /// <summary>普通宝库（G1-G8 等）</summary>
    Normal,

    /// <summary>神殿类（有召唤祭坛）</summary>
    Shrine,

    /// <summary>运河类</summary>
    Canal,

    /// <summary>深层绿图</summary>
    DeepGreen,
}

/// <summary>
/// 宝库目录。编号来自 Trig XML，已在 7.57 实测 1279=巡梦金库 可用。
/// </summary>
public static class TreasureDungeons
{
    public static readonly IReadOnlyList<TreasureDungeon> All = new[]
    {
        // G1-G8
        new TreasureDungeon("水城宝物库", DungeonFamily.Normal, new uint[] { 558 }),
        new TreasureDungeon("运河宝物库", DungeonFamily.Normal, new uint[] { 712 }),
        new TreasureDungeon("惊奇百宝城", DungeonFamily.Normal, new uint[] { 960 }),
        new TreasureDungeon("梦羽宝境", DungeonFamily.Normal, new uint[] { 794, 924, 1123 }),

        // 深层绿
        new TreasureDungeon("运河宝物库深层", DungeonFamily.DeepGreen, new uint[] { 725 }),

        // 神殿
        new TreasureDungeon("运河宝物库神殿", DungeonFamily.Shrine, new uint[] { 613, 614, 620, 621, 622 }),
        new TreasureDungeon("梦羽宝殿", DungeonFamily.Shrine, new uint[] { 879 }),
        new TreasureDungeon("厄尔庇斯育体宝殿", DungeonFamily.Shrine, new uint[] { 1000 }),
        new TreasureDungeon("巡梦金库", DungeonFamily.Shrine, new uint[] { 1209, 1279 }),

        // G10-G18
        new TreasureDungeon("加加财富天坑", DungeonFamily.Normal, new uint[] { 1187, 1188, 1189, 1190, 1191, 1192 }),
        new TreasureDungeon("加加财富天坑·深层", DungeonFamily.Normal, new uint[] { 813, 814, 815, 816, 817, 818 }),
        new TreasureDungeon("加加财富天坑·更深处", DungeonFamily.Normal, new uint[] { 956, 957, 958, 959, 961 }),
    };

    private static readonly Dictionary<uint, TreasureDungeon> byTerritory = Build();

    private static Dictionary<uint, TreasureDungeon> Build()
    {
        var d = new Dictionary<uint, TreasureDungeon>();
        foreach (var dg in All)
            foreach (var tt in dg.TerritoryTypes)
                d[tt] = dg;
        return d;
    }

    public static bool IsTreasureTerritory(uint territoryType)
        => byTerritory.ContainsKey(territoryType);

    public static TreasureDungeon? Find(uint territoryType)
        => byTerritory.TryGetValue(territoryType, out var v) ? v : null;
}
