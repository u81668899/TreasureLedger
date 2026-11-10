using System;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Teleport;

/// <summary>
/// 传送相关的配置。
/// </summary>
public sealed class TeleportConfig
{
    /// <summary>启用传送提醒（聊天栏）。</summary>
    public bool EnableNotify { get; set; } = true;

    /// <summary>启用自动确认。</summary>
    public bool EnableAutoConfirm { get; set; } = false;

    /// <summary>
    /// 只在「确认自己在挖宝队伍」时生效。
    /// 关闭则任何时候都生效（不推荐）。
    /// </summary>
    public bool OnlyInTreasureParty { get; set; } = true;

    /// <summary>
    /// 加入招募后，多久内仍视为「在挖宝队伍」。
    /// 防止进本后再出来时状态丢失。
    /// </summary>
    public int TreasurePartyWindowMinutes { get; set; } = 180;
}

/// <summary>
/// 挖宝队伍判定。
///
/// ══════════════════════════════════════════════════════════════
/// 判定规则（照搬 Triggernometry XML「2.0招募进组重置」）
///
/// 三个条件同时满足才算「在挖宝队伍」：
///   1. 是在招募进组时加入的（不是随便组队）
///   2. 当前仍在队伍中（PartyList.Length &gt; 1）
///   3. 那支队伍的招募被判定为【寻宝招募】
///
/// 「寻宝招募」的判定（XML 原文逻辑）：
///   a) 招募标题以「寻宝」开头         → 是
///   b) 招募标题以「讨伐歼灭战：」开头
///      且 简介里匹配到挖宝特征        → 是
///         特征正则：((G|g)\d{1,2}|深绿|绿图)
///                   (( |:|：|/|、|\\|\.|。|`|·|，|,|-|—|；|;|　){0,3}
///                   \d{1,2}(张|个|)(图|t|T|)
///         也就是「G18 12张图」「深绿 5t」这类写法
///   c) 特例：那布里亚勒斯讨伐战（常见挖宝队挂的讨伐名）
///
/// 为什么会这样：挖宝招募经常挂「讨伐歼灭战：那布里亚勒斯讨伐战」，
/// 因为要开副本。所以不能只看标题，必须看简介。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed class TreasurePartyDetector
{
    private readonly IPluginLog log;
    private readonly IPartyList partyList;

    /// <summary>判定为寻宝招募的标题正则。</summary>
    private static readonly Regex TreasureTitleRegex = new(@"^寻宝", RegexOptions.Compiled);

    /// <summary>讨伐歼灭战标题前缀。</summary>
    private static readonly Regex DutyTitleRegex = new(@"^讨伐歼灭战：", RegexOptions.Compiled);

    /// <summary>挖宝特征（出现在简介里）。</summary>
    private static readonly Regex TreasureDescRegex = new(
        @"((G|g)\d{1,2}|深绿|绿图)(( |:|：|/|、|\\|\.|。|`|·|，|,|-|—|；|;|　){0,3}\d{1,2}(张|个|)(图|t|T|)|[^0-9]*\d{1,2}(张|个|)(图|t|T|)|[^0-9]*挖)",
        RegexOptions.Compiled);

    /// <summary>已知的挖宝队常用讨伐名。</summary>
    private const string KnownDutyName = "讨伐歼灭战：那布里亚勒斯讨伐战";

    /// <summary>加入招募的时间。</summary>
    private DateTime joinedAt = DateTime.MinValue;

    /// <summary>那次招募的标题。</summary>
    public string JoinedTitle { get; private set; } = string.Empty;

    /// <summary>那次招募的简介。</summary>
    public string JoinedDescription { get; private set; } = string.Empty;

    /// <summary>那次招募是否被判定为寻宝招募。</summary>
    public bool IsTreasureRecruit { get; private set; }

    /// <summary>判定依据，给 UI 显示。</summary>
    public string VerdictReason { get; private set; } = string.Empty;

    public TreasurePartyDetector(IPluginLog log, IPartyList partyList)
    {
        this.log = log;
        this.partyList = partyList;
    }

    /// <summary>在小队里。</summary>
    public bool InParty
    {
        get
        {
            try { return partyList.Length > 1; }
            catch { return false; }
        }
    }

    public int PartySize
    {
        get
        {
            try { return partyList.Length; }
            catch { return 0; }
        }
    }

    /// <summary>
    /// 记录一次「通过招募加入队伍」。
    /// 由聊天日志「加入了XX组建的"YYY"队伍」触发。
    /// </summary>
    /// <param name="title">招募标题（日志里的 YYY）。</param>
    /// <param name="description">招募简介，来自招募板或上次缓存的招募内容。</param>
    public void OnJoinedRecruit(string title, string description)
    {
        joinedAt = DateTime.Now;
        JoinedTitle = title ?? string.Empty;
        JoinedDescription = description ?? string.Empty;

        IsTreasureRecruit = Judge(title ?? string.Empty, description ?? string.Empty, out var reason);
        VerdictReason = reason;

        log.Information(
            "[TreasureLedger] 加入招募：标题=「{Title}」→ {Verdict}（{Reason}）",
            JoinedTitle, IsTreasureRecruit ? "寻宝招募" : "非寻宝招募", reason);
    }

    /// <summary>清除队伍状态。</summary>
    public void Clear()
    {
        joinedAt = DateTime.MinValue;
        JoinedTitle = string.Empty;
        JoinedDescription = string.Empty;
        IsTreasureRecruit = false;
        VerdictReason = string.Empty;
    }

    /// <summary>
    /// 判断是否处于挖宝队伍中。
    /// </summary>
    public bool IsTreasureParty(int windowMinutes)
    {
        // 必须真的在队伍里
        if (!InParty) return false;

        // 必须是招募进来的
        if (joinedAt == DateTime.MinValue) return false;

        // 必须被判为寻宝招募
        if (!IsTreasureRecruit) return false;

        // 窗口内
        return DateTime.Now - joinedAt <= TimeSpan.FromMinutes(windowMinutes);
    }

    /// <summary>
    /// 判定一支招募是不是寻宝招募。
    /// 逻辑照搬 XML 的「2.0招募进组重置」。
    /// </summary>
    public static bool Judge(string title, string description, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(title))
        {
            reason = "标题为空";
            return false;
        }

        var t = title.Trim();
        var d = (description ?? string.Empty).Trim();

        // 1) 标题以「寻宝」开头 → 直接认定
        if (TreasureTitleRegex.IsMatch(t))
        {
            reason = "标题以「寻宝」开头";
            return true;
        }

        // 2) 讨伐歼灭战：那布里亚勒斯讨伐战 → 常见挖宝队
        if (string.Equals(t, KnownDutyName, StringComparison.OrdinalIgnoreCase))
        {
            reason = "已知挖宝队常用讨伐名";
            return true;
        }

        // 3) 讨伐歼灭战开头 + 简介里有挖宝特征
        if (DutyTitleRegex.IsMatch(t))
        {
            if (d.Length > 0 && TreasureDescRegex.IsMatch(d))
            {
                reason = "讨伐标题 + 简介含挖宝特征";
                return true;
            }

            reason = "讨伐标题但简介没有挖宝特征";
            return false;
        }

        // 4) 其他标题，看简介有没有挖宝特征
        if (d.Length > 0 && TreasureDescRegex.IsMatch(d))
        {
            reason = "简介含挖宝特征";
            return true;
        }

        reason = "标题与简介都不像挖宝招募";
        return false;
    }
}
