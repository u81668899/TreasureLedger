using System;
using System.Collections.Generic;
using System.Linq;

namespace TreasureLedger.Maps;

/// <summary>宝图家庭分类。</summary>
public enum DungeonFamily
{
    /// <summary>普通宝库（G1-G8 / G10 / G12 / G14-G18）</summary>
    Normal,

    /// <summary>神殿类（有召唤祭坛，G10/G12/G15 用上级中级下级，G17-G18 用大梦终梦）</summary>
    Shrine,

    /// <summary>运河类</summary>
    Canal,

    /// <summary>深层绿图</summary>
    DeepGreen,
}

/// <summary>
/// 一个宝库副本的定义。
///
/// BottomBosses：下底 boss 名（判定「到底了」）。
///   ACT 那边因为掉线日志重叠导致层数错乱，只能靠 boss 名判断；
///   卫月这边同样用 boss 名作为主判据，因为它唯一且不重复。
///
/// MaxFloor：该图最深能到第几层（用于校验收到的层数）。
/// </summary>
public sealed record TreasureDungeon(
    string Name,
    DungeonFamily Family,
    IReadOnlyList<uint> TerritoryTypes,
    IReadOnlyList<string> BottomBosses,
    int MaxFloor = 0);

/// <summary>
/// 宝库目录。
/// TerritoryType 编号与 ACT/Triggernometry 通用（已实测 1279=巡梦金库、1192=加加财富天坑）。
/// </summary>
public static class TreasureDungeons
{
    public static readonly IReadOnlyList<TreasureDungeon> All = new[]
    {
        // ── G1-G8 系列 ──
        new TreasureDungeon("水城宝物库",    DungeonFamily.Normal, new uint[] { 558 },  Array.Empty<string>()),
        new TreasureDungeon("运河宝物库",    DungeonFamily.Normal, new uint[] { 712 },  Array.Empty<string>()),
        new TreasureDungeon("惊奇百宝城",    DungeonFamily.Normal, new uint[] { 960 },  Array.Empty<string>()),
        new TreasureDungeon("梦羽宝境",      DungeonFamily.Normal, new uint[] { 794, 924, 1123 }, Array.Empty<string>()),

        // ── 深层绿图 ──
        new TreasureDungeon("运河宝物库深层", DungeonFamily.DeepGreen, new uint[] { 725 }, Array.Empty<string>()),

        // ── 神殿类 ──
        new TreasureDungeon("运河宝物库神殿", DungeonFamily.Shrine,
            new uint[] { 613, 614, 620, 621, 622 },
            new[] { "神殿曼提克", "神殿变种龙", "神殿阿班达" }),

        new TreasureDungeon("梦羽宝殿", DungeonFamily.Shrine,
            new uint[] { 879 },
            new[] { "代恩·奥瑟", "视财如命 代恩·奥瑟" }),

        new TreasureDungeon("厄尔庇斯育体宝殿", DungeonFamily.Shrine,
            new uint[] { 1000 },
            new[] { "纳西索斯", "法厄同", "希波墨涅斯" }),

        new TreasureDungeon("巡梦金库", DungeonFamily.Shrine,
            new uint[] { 1209, 1279 },
            new[] { "纳西索斯", "法厄同", "希波墨涅斯", "雷电之魂", "移动要塞", "六足炮台" }),

        // ── G10 系列（加加财富天坑一层）──
        new TreasureDungeon("加加财富天坑", DungeonFamily.Normal,
            new uint[] { 1187, 1188, 1189, 1190, 1191, 1192 },
            new[] { "雷电之魂", "移动要塞", "六足炮台" }),

        // ── G12 系列 ──
        new TreasureDungeon("加加财富天坑·深层", DungeonFamily.Normal,
            new uint[] { 813, 814, 815, 816, 817, 818 },
            new[] { "雷电之魂", "移动要塞", "六足炮台" }),

        // ── G14-G18 系列 ──
        new TreasureDungeon("加加财富天坑·更深处", DungeonFamily.Normal,
            new uint[] { 956, 957, 958, 959, 961 },
            new[] { "雷电之魂", "移动要塞", "六足炮台" }),
    };

    private static readonly Dictionary<uint, TreasureDungeon> byTerritory = Build();
    private static readonly Dictionary<string, TreasureDungeon> byBoss = BuildBossMap();

    private static Dictionary<uint, TreasureDungeon> Build()
    {
        var d = new Dictionary<uint, TreasureDungeon>();
        foreach (var dg in All)
            foreach (var tt in dg.TerritoryTypes)
                d[tt] = dg;
        return d;
    }

    private static Dictionary<string, TreasureDungeon> BuildBossMap()
    {
        var d = new Dictionary<string, TreasureDungeon>(StringComparer.OrdinalIgnoreCase);
        foreach (var dg in All)
            foreach (var b in dg.BottomBosses)
                d[b] = dg;
        return d;
    }

    public static bool IsTreasureTerritory(uint territoryType) => byTerritory.ContainsKey(territoryType);

    public static TreasureDungeon? Find(uint territoryType)
        => byTerritory.TryGetValue(territoryType, out var v) ? v : null;

    /// <summary>按 boss 名判断是不是下底 boss。</summary>
    public static bool IsBottomBoss(string name, out TreasureDungeon? dungeon)
    {
        dungeon = null;
        if (string.IsNullOrWhiteSpace(name)) return false;

        foreach (var dg in All)
        {
            foreach (var b in dg.BottomBosses)
            {
                if (name.Contains(b, StringComparison.OrdinalIgnoreCase))
                {
                    dungeon = dg;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>该宝图是否使用「大梦/终梦」体系（G17-G18）而非「上级/中级/下级」。</summary>
    public static bool UsesDreamTiers(TreasureDungeon? dg)
        => dg is not null && dg.Family == DungeonFamily.Shrine && dg.TerritoryTypes.Any(t => t is 1209 or 1279);
}
