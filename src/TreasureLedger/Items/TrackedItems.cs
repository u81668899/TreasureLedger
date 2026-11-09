using System;
using System.Collections.Generic;

namespace TreasureLedger.Items;

/// <summary>
/// 统计清单中的一件物品。Display 是聊天栏里显示用的短名。
/// </summary>
public sealed record TrackedItem(uint ItemId, string FullName, string Display);

/// <summary>
/// 挖宝掉落清单，顺序即播报顺序，与 Triggernometry XML 一致。
/// </summary>
public static class TrackedItems
{
    /// <summary>XML 里按这个顺序拼「本次掉落内容」。</summary>
    public static readonly IReadOnlyList<TrackedItem> All = new[]
    {
        new TrackedItem(8156,  "鞣革制的隐藏地图", "单人绿"),
        new TrackedItem(20270, "纳夏薄绢",         "纳夏薄绢"),
        new TrackedItem(19770, "深层传送魔纹的地图", "深层绿"),
        new TrackedItem(27977, "附魔榆木木材",     "附魔木"),
        new TrackedItem(28905, "后臀革",           "后臀"),
        new TrackedItem(30135, "顺柔剂",           "顺柔"),
        new TrackedItem(31091, "南洋织物",         "南洋"),
        new TrackedItem(33027, "小牛皮",           "小牛皮"),
        new TrackedItem(33869, "前沿布料",         "前沿"),
        new TrackedItem(32203, "古菩台灯",         "古菩灯"),
        new TrackedItem(32858, "黄金大河狸",       "黄金河狸"),
        new TrackedItem(21922, "金鲶",             "金鲶"),
        new TrackedItem(36628, "开心布",           "开心布"),
        new TrackedItem(36341, "大天使之翼",       "大天使"),
        new TrackedItem(36343, "仙子族之翼",       "仙子族"),
        new TrackedItem(37484, "白色花边阳伞",     "白伞"),
        new TrackedItem(37491, "绚烂华丽的羽毛",   "羽毛"),
        new TrackedItem(37488, "惊奇纤维",         "惊奇纤维"),
        new TrackedItem(38686, "惊奇革",           "惊奇革"),
        new TrackedItem(39589, "净洁布",           "净洁布"),
        new TrackedItem(39592, "灼热的火角",       "火角"),
        new TrackedItem(40700, "轻柔棉布",         "轻柔"),
        new TrackedItem(41653, "竞技布",           "竞技布"),
        new TrackedItem(39590, "惊奇补剂",         "惊奇补剂"),
        new TrackedItem(39484, "迷你艾多纽斯",     "迷你艾多"),
        new TrackedItem(38453, "迷你好友",         "迷你好友"),
        new TrackedItem(43606, "发型样式：二分区", "发:二分区"),
        new TrackedItem(44322, "管弦乐琴乐谱：此路不通", "谱:不通"),
        new TrackedItem(44132, "图拉尔古布",       "图拉布"),
        new TrackedItem(44257, "缎带阳伞",         "缎带伞"),
        new TrackedItem(44133, "辉鳞晶",           "辉鳞晶"),
        new TrackedItem(41805, "暮光宝石",         "暮光石"),
        new TrackedItem(44506, "发型样式：微卷短发", "发:卷短发"),
        new TrackedItem(45070, "幻梦武器箱",       "幻武箱"),
        new TrackedItem(45072, "灯芯绒布",         "灯芯布"),
        new TrackedItem(48229, "弹性皮带",         "弹皮带"),
        new TrackedItem(48228, "分光性染剂",       "分光染剂"),
        new TrackedItem(48225, "管弦乐琴乐谱：诙谐的笑谈", "谱：笑谈"),
        new TrackedItem(48152, "面部配饰：豹目眼镜", "面：豹眼镜"),
        new TrackedItem(46107, "健身地垫",         "健身地垫"),
        new TrackedItem(46171, "永护塔路灯",       "永护灯"),
        new TrackedItem(46773, "能量饮料GG",       "饮料GG"),
        new TrackedItem(46147, "管弦乐琴乐谱：铭记", "谱：铭记"),
        new TrackedItem(46829, "羊角笛",           "羊角笛"),
        new TrackedItem(46842, "面部配饰：战术护目镜", "面：战术护目镜"),
        new TrackedItem(46845, "面部配饰：教授眼镜", "面：教授眼镜"),
        new TrackedItem(46848, "火炬",             "火炬"),
        new TrackedItem(47964, "小小羊",           "小小羊"),
        new TrackedItem(47965, "索取小猫",         "索取小猫"),
        new TrackedItem(50795, "管弦乐琴乐谱：夹饼欢宴", "谱：夹饼"),
        new TrackedItem(50797, "管弦乐琴乐谱：勇武", "谱：勇武"),
        new TrackedItem(49846, "永护塔混音台",     "混音台"),
        new TrackedItem(49847, "羊羊壁挂霓虹灯",   "羊挂灯"),
        new TrackedItem(50830, "神话传令官布料",   "神话布"),
        new TrackedItem(52357, "管弦乐琴乐谱：为了守护笑容", "谱：守护笑容"),
        new TrackedItem(51262, "永护投影屏",       "投影屏"),
        new TrackedItem(51263, "永护立体声音箱",   "立体音箱"),
        new TrackedItem(52630, "运动布",           "运动布"),
        new TrackedItem(52631, "靛蓝斜纹布",       "蓝斜纹布"),
    };

    private static readonly Dictionary<uint, TrackedItem> byId = Build();

    private static Dictionary<uint, TrackedItem> Build()
    {
        var d = new Dictionary<uint, TrackedItem>(All.Count);
        foreach (var it in All) d[it.ItemId] = it;
        return d;
    }

    public static TrackedItem? Find(uint itemId) => byId.TryGetValue(itemId, out var v) ? v : null;

    /// <summary>统计清单里所有物品 ID。</summary>
    public static IEnumerable<uint> AllIds()
    {
        foreach (var it in All) yield return it.ItemId;
    }
}
