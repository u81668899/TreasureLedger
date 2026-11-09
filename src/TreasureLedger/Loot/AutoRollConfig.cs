using System;
using System.Collections.Generic;

namespace TreasureLedger.Loot;

/// <summary>单件物品的投掷策略。</summary>
public enum RollPolicy
{
    /// <summary>跟默认一致</summary>
    Default = 0,

    /// <summary>需求</summary>
    Need = 1,

    /// <summary>贪婪</summary>
    Greed = 2,

    /// <summary>放弃（不拿）</summary>
    Pass = 3,
}

/// <summary>黑白名单模式。</summary>
public enum RollListMode
{
    /// <summary>白名单：名单内用「指定策略」，名单外走默认</summary>
    Whitelist = 0,

    /// <summary>黑名单：名单内一律放弃，名单外走默认</summary>
    Blacklist = 1,
}

/// <summary>
/// 自动投掷的配置。
/// 默认关闭 —— 这是会动游戏的写操作，必须玩家主动开启。
/// </summary>
public sealed class AutoRollConfig
{
    /// <summary>总开关。默认关。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 是否只在挖宝场景生效。
    /// 注意：挖宝的投掷不只在宝库里出现 ——
    /// 在野外打宝库看门怪（触发宝物库的那个怪）时也会掉东西要 roll。
    /// 所以这里的判断是「宝库副本内 或 最近有挖宝活动」，而不是单纯的副本判断。
    /// </summary>
    public bool OnlyInTreasureContext { get; set; } = true;

    /// <summary>
    /// 野外挖宝活动的判定窗口。
    /// 在野外打了宝图怪、开了宝图箱之后的一段时间内，仍算挖宝场景。
    /// </summary>
    public int TreasureContextMinutes { get; set; } = 30;

    /// <summary>名单外物品的默认策略。</summary>
    public RollPolicy DefaultPolicy { get; set; } = RollPolicy.Greed;

    /// <summary>黑白名单模式。</summary>
    public RollListMode ListMode { get; set; } = RollListMode.Whitelist;

    /// <summary>白名单内物品用的策略。</summary>
    public RollPolicy WhitelistPolicy { get; set; } = RollPolicy.Need;

    /// <summary>物品 ID → 单独指定的策略（优先级最高）。</summary>
    public Dictionary<uint, RollPolicy> ItemPolicies { get; set; } = new();

    /// <summary>两次投掷之间的最小间隔（毫秒），避免动作过快。</summary>
    public int IntervalMs { get; set; } = 300;

    /// <summary>
    /// 自定义投掷函数特征码（可选）。
    /// 游戏大版本更新后，若内置特征码失效，
    /// 可填入社区提供的新特征码，不必等插件更新。
    /// </summary>
    public string CustomSignature { get; set; } = string.Empty;

    /// <summary>解析某件物品该用哪个策略。</summary>
    public RollPolicy ResolvePolicy(uint itemId)
    {
        // 显式指定的最高优先
        if (ItemPolicies.TryGetValue(itemId, out var explicitPolicy)
            && explicitPolicy != RollPolicy.Default)
        {
            return explicitPolicy;
        }

        // 名单判断
        var inList = ItemPolicies.ContainsKey(itemId);

        return ListMode switch
        {
            RollListMode.Whitelist when inList => WhitelistPolicy,
            RollListMode.Whitelist => DefaultPolicy,
            RollListMode.Blacklist when inList => RollPolicy.Pass,
            RollListMode.Blacklist => DefaultPolicy,
            _ => DefaultPolicy,
        };
    }
}
