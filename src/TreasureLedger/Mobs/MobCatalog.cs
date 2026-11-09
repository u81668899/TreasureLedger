using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TreasureLedger.Mobs;

/// <summary>小怪分类。</summary>
public enum MobCategory
{
    /// <summary>顺序怪（洋葱/茄子/蒜头/番茄/王后），对应攻击标记 1-5。</summary>
    Order,

    /// <summary>金币怪（盗宝团），掉金币。</summary>
    Treasure,

    /// <summary>战斗怪（大象类），会打人。</summary>
    Battle,
}

/// <summary>一个被追踪的小怪条目。</summary>
public sealed record MobDefinition(
    string Name,
    MobCategory Category,
    int MarkSlot = 0);

/// <summary>
/// 小怪名单。
/// 全部取自 Triggernometry XML 里「8.2洋葱标记」「8.3寻宝标记」和怪物记录触发器的正则，
/// 一个不多一个不少。
/// </summary>
public static class MobCatalog
{
    /// <summary>
    /// 顺序怪：分五组，按顺序给攻击标记 1-5。
    /// 组内名字是「前缀 + 品种」，前缀随宝库版本变。
    /// </summary>
    private static readonly (int Slot, string Variant, string[] Prefixes)[] OrderGroups =
    {
        (1, "洋葱", new[] { "运河", "神殿", "宝库", "神秘", "惊奇", "育体", "图拉尔", "金库" }),
        (2, "茄子", new[] { "运河", "神殿", "宝库", "神秘", "惊奇", "育体", "图拉尔", "金库" }),
        (3, "蒜头", new[] { "运河", "神殿", "宝库", "神秘", "惊奇", "育体", "图拉尔", "金库" }),
        (4, "番茄", new[] { "运河", "神殿", "宝库", "神秘", "惊奇", "育体", "图拉尔", "金库" }),
        (5, "王后", new[] { "运河", "神殿", "宝库", "神秘", "惊奇", "育体", "图拉尔", "金库" }),
    };

    /// <summary>金币怪。</summary>
    private static readonly string[] TreasureMobs =
    {
        "哥布林寻宝猎人", "鲶鱼精盗贼", "金鲶", "水妖魔术师",
        "黄金协助机", "育体拉姆帕斯", "财富羊驼", "黄金猫",
    };

    /// <summary>战斗怪（大象类）。</summary>
    private static readonly string[] BattleMobs =
    {
        "阿尔戈斯", "艾卜哈拉木", "神殿象魔", "宝库守护者",
        "七彩巨像", "育体吕萨", "财富呜噜怪", "金库守卫",
    };

    /// <summary>名字 → 定义。构建时把顺序怪的所有组合展开。</summary>
    private static readonly Dictionary<string, MobDefinition> ByName = Build();

    private static Dictionary<string, MobDefinition> Build()
    {
        var d = new Dictionary<string, MobDefinition>(StringComparer.Ordinal);

        foreach (var (slot, variant, prefixes) in OrderGroups)
        {
            foreach (var p in prefixes)
            {
                // 注意：育体大蒜 是 XML 里的特例写法，不是「育体蒜头」
                var name = (p == "育体" && variant == "蒜头") ? "育体大蒜" : p + variant;
                d[name] = new MobDefinition(name, MobCategory.Order, slot);
            }
        }

        foreach (var n in TreasureMobs)
            d[n] = new MobDefinition(n, MobCategory.Treasure);

        foreach (var n in BattleMobs)
            d[n] = new MobDefinition(n, MobCategory.Battle);

        return d;
    }

    public static MobDefinition? Find(string name)
        => !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out var v) ? v : null;

    public static IReadOnlyCollection<MobDefinition> All => ByName.Values;
}

/// <summary>
/// 小怪出现/成功/失败统计。
///
/// 判定方式：
///   出现 → ObjectTable 里新出现一个匹配名单的 BattleNpc
///   成功 → 该对象消失时处于死亡状态
///   失败 → 该对象消失时仍然活着（逃走/消失）
///
/// 为什么要缓存「最后是否死亡」：
/// 对象一旦从 ObjectTable 移除就拿不到状态了，
/// 所以每帧扫描时记录 IsDead，消失时用最后一次记录判断。
/// </summary>
public sealed class MobRecord
{
    public int OrderSuccess { get; set; }
    public int OrderFail { get; set; }
    public int TreasureSuccess { get; set; }
    public int TreasureFail { get; set; }
    public int BattleSuccess { get; set; }
    public int BattleFail { get; set; }

    public int SuccessOf(MobCategory c) => c switch
    {
        MobCategory.Order => OrderSuccess,
        MobCategory.Treasure => TreasureSuccess,
        MobCategory.Battle => BattleSuccess,
        _ => 0,
    };

    public int FailOf(MobCategory c) => c switch
    {
        MobCategory.Order => OrderFail,
        MobCategory.Treasure => TreasureFail,
        MobCategory.Battle => BattleFail,
        _ => 0,
    };

    public void Add(MobCategory c, bool success)
    {
        switch (c)
        {
            case MobCategory.Order:
                if (success) OrderSuccess++; else OrderFail++;
                break;
            case MobCategory.Treasure:
                if (success) TreasureSuccess++; else TreasureFail++;
                break;
            case MobCategory.Battle:
                if (success) BattleSuccess++; else BattleFail++;
                break;
        }
    }

    public bool IsEmpty
        => OrderSuccess + OrderFail + TreasureSuccess + TreasureFail
           + BattleSuccess + BattleFail == 0;

    public void Clear()
    {
        OrderSuccess = OrderFail = 0;
        TreasureSuccess = TreasureFail = 0;
        BattleSuccess = BattleFail = 0;
    }

    public string Summary()
    {
        var parts = new List<string>();
        if (OrderSuccess + OrderFail > 0) parts.Add($"顺序怪 {OrderSuccess}成/{OrderFail}败");
        if (TreasureSuccess + TreasureFail > 0) parts.Add($"金币怪 {TreasureSuccess}成/{TreasureFail}败");
        if (BattleSuccess + BattleFail > 0) parts.Add($"战斗怪 {BattleSuccess}成/{BattleFail}败");
        return parts.Count == 0 ? string.Empty : string.Join("　", parts);
    }
}
