using System;
using System.Collections.Generic;
using System.Linq;

namespace TreasureLedger.Items;

/// <summary>
/// 一件被统计的掉落物品。Display 是聊天栏里显示用的短名。
/// Tier 是所属宝图分类，UserAdded 标记是否用户手动添加。
/// </summary>
public sealed record TrackedItem(
    uint ItemId,
    string FullName,
    string Display,
    MapTier Tier,
    bool UserAdded = false);

/// <summary>
/// 内置掉落清单，分类与顺序严格对齐 Triggernometry XML 的六个统计内核。
/// 用户可以增删，不影响这里的默认值。
/// </summary>
public static class TrackedItems
{
    public static readonly IReadOnlyList<TrackedItem> Builtin = new[]
    {
        // ── G1-G8 ──
        new TrackedItem(8156,  "鞣革制的隐藏地图", "单人绿", MapTier.G1G8),

        // ── G10 ──
        new TrackedItem(19770, "深层传送魔纹的地图", "深层绿", MapTier.G10),
        new TrackedItem(20270, "纳夏薄绢",           "纳夏薄绢", MapTier.G10),

        // ── G12 ──
        new TrackedItem(33869, "前沿布料",   "前沿",   MapTier.G12),
        new TrackedItem(31091, "南洋织物",   "南洋",   MapTier.G12),
        new TrackedItem(32203, "古菩台灯",   "古菩灯", MapTier.G12),
        new TrackedItem(28905, "后臀革",     "后臀",   MapTier.G12),
        new TrackedItem(33027, "小牛皮",     "小牛皮", MapTier.G12),
        new TrackedItem(27977, "附魔榆木木材", "附魔木", MapTier.G12),
        new TrackedItem(30135, "顺柔剂",     "顺柔",   MapTier.G12),
        new TrackedItem(32858, "黄金大河狸", "黄金河狸", MapTier.G12),

        // ── G14-G15 ──
        new TrackedItem(36343, "仙子族之翼",     "仙子族", MapTier.G14G15),
        new TrackedItem(39589, "净洁布",         "净洁布", MapTier.G14G15),
        new TrackedItem(36341, "大天使之翼",     "大天使", MapTier.G14G15),
        new TrackedItem(36628, "开心布",         "开心布", MapTier.G14G15),
        new TrackedItem(37488, "惊奇纤维",       "惊奇纤维", MapTier.G14G15),
        new TrackedItem(39590, "惊奇补剂",       "惊奇补剂", MapTier.G14G15),
        new TrackedItem(38686, "惊奇革",         "惊奇革", MapTier.G14G15),
        new TrackedItem(39592, "灼热的火角",     "火角",   MapTier.G14G15),
        new TrackedItem(37484, "白色花边阳伞",   "白伞",   MapTier.G14G15),
        new TrackedItem(41653, "竞技布",         "竞技布", MapTier.G14G15),
        new TrackedItem(37491, "绚烂华丽的羽毛", "羽毛",   MapTier.G14G15),
        new TrackedItem(40700, "轻柔棉布",       "轻柔",   MapTier.G14G15),
        new TrackedItem(38453, "迷你好友",       "迷你好友", MapTier.G14G15),
        new TrackedItem(39484, "迷你艾多纽斯",   "迷你艾多", MapTier.G14G15),

        // ── 深层绿 ──
        new TrackedItem(21922, "金鲶", "金鲶", MapTier.DeepGreen),

        // ── G17-G18 ──
        new TrackedItem(46107, "健身地垫",       "健身地垫", MapTier.G17G18),
        new TrackedItem(48228, "分光性染剂",     "分光染剂", MapTier.G17G18),
        new TrackedItem(44934, "加加财富树",     "财富树",   MapTier.G17G18),
        new TrackedItem(43606, "发型样式：二分区", "发:二分区", MapTier.G17G18),
        new TrackedItem(44506, "发型样式：微卷短发", "发:卷短发", MapTier.G17G18),
        new TrackedItem(43572, "吸蜜怪",         "吸蜜怪",   MapTier.G17G18),
        new TrackedItem(44132, "图拉尔古布",     "图拉布",   MapTier.G17G18),
        new TrackedItem(47964, "小小羊",         "小小羊",   MapTier.G17G18),
        new TrackedItem(48104, "巧儿陆行鸟喷泉", "巧儿喷泉", MapTier.G17G18),
        new TrackedItem(45070, "幻梦武器箱",     "幻武箱",   MapTier.G17G18),
        new TrackedItem(48229, "弹性皮带",       "弹皮带",   MapTier.G17G18),
        new TrackedItem(41805, "暮光宝石",       "暮光石",   MapTier.G17G18),
        new TrackedItem(49846, "永护塔混音台",   "混音台",   MapTier.G17G18),
        new TrackedItem(46171, "永护塔路灯",     "永护灯",   MapTier.G17G18),
        new TrackedItem(51262, "永护投影屏",     "投影屏",   MapTier.G17G18),
        new TrackedItem(51263, "永护立体声音箱", "立体音箱", MapTier.G17G18),
        new TrackedItem(46848, "火炬",           "火炬",     MapTier.G17G18),
        new TrackedItem(45072, "灯芯绒布",       "灯芯布",   MapTier.G17G18),
        new TrackedItem(50830, "神话传令官布料", "神话布",   MapTier.G17G18),
        new TrackedItem(52357, "管弦乐琴乐谱：为了守护笑容", "谱：守护笑容", MapTier.G17G18),
        new TrackedItem(50797, "管弦乐琴乐谱：勇武", "谱：勇武", MapTier.G17G18),
        new TrackedItem(50795, "管弦乐琴乐谱：夹饼欢宴", "谱：夹饼", MapTier.G17G18),
        new TrackedItem(44323, "管弦乐琴乐谱：开路先锋", "谱：开路", MapTier.G17G18),
        new TrackedItem(45038, "管弦乐琴乐谱：暗夜投石", "谱：暗夜", MapTier.G17G18),
        new TrackedItem(44322, "管弦乐琴乐谱：此路不通", "谱：不通", MapTier.G17G18),
        new TrackedItem(48225, "管弦乐琴乐谱：诙谐的笑谈", "谱：笑谈", MapTier.G17G18),
        new TrackedItem(46147, "管弦乐琴乐谱：铭记", "谱：铭记", MapTier.G17G18),
        new TrackedItem(47965, "索取小猫",       "索取小猫", MapTier.G17G18),
        new TrackedItem(44257, "缎带阳伞",       "缎带伞",   MapTier.G17G18),
        new TrackedItem(49847, "羊羊壁挂霓虹灯", "羊挂灯",   MapTier.G17G18),
        new TrackedItem(46829, "羊角笛",         "羊角笛",   MapTier.G17G18),
        new TrackedItem(46773, "能量饮料GG",     "饮料GG",   MapTier.G17G18),
        new TrackedItem(44133, "辉鳞晶",         "辉鳞晶",   MapTier.G17G18),
        new TrackedItem(52630, "运动布",         "运动布",   MapTier.G17G18),
        new TrackedItem(44269, "金子堆",         "金子堆",   MapTier.G17G18),
        new TrackedItem(44494, "金曦绿茵号",     "金曦号",   MapTier.G17G18),
        new TrackedItem(52631, "靛蓝斜纹布",     "蓝斜纹布", MapTier.G17G18),
        new TrackedItem(46842, "面部配饰：战术护目镜", "面：战术护目镜", MapTier.G17G18),
        new TrackedItem(46845, "面部配饰：教授眼镜", "面：教授眼镜", MapTier.G17G18),
        new TrackedItem(45005, "面部配饰：猫眼眼镜", "面：猫眼镜", MapTier.G17G18),
        new TrackedItem(45006, "面部配饰：窄框眼镜", "面：窄框眼镜", MapTier.G17G18),
        new TrackedItem(48152, "面部配饰：豹目眼镜", "面：豹眼镜", MapTier.G17G18),
    };

    /// <summary>内置 ID → 物品。</summary>
    private static readonly Dictionary<uint, TrackedItem> builtinById =
        Builtin.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.First());

    public static TrackedItem? FindBuiltin(uint itemId)
        => builtinById.TryGetValue(itemId, out var v) ? v : null;

    /// <summary>按分类分组的内置物品。</summary>
    public static IEnumerable<(MapTier Tier, List<TrackedItem> Items)> ByTier()
    {
        foreach (var tier in Enum.GetValues<MapTier>())
        {
            var list = Builtin.Where(x => x.Tier == tier).ToList();
            if (list.Count > 0) yield return (tier, list);
        }
    }

    public static IEnumerable<uint> AllIds() => builtinById.Keys;
}
