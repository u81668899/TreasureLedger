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
    /// 只在「确认自己在挖宝小队」时生效。
    /// 关闭则任何时候都生效（不推荐）。
    /// </summary>
    public bool OnlyInTreasureParty { get; set; } = true;

    /// <summary>
    /// 挖宝迹象的有效期。
    /// 打怪、开宝图、进宝库之后，这段时间内都算挖宝小队。
    /// </summary>
    public int TreasureEvidenceMinutes { get; set; } = 180;
}

/// <summary>
/// 挖宝小队判定。
///
/// ══════════════════════════════════════════════════════════════
/// 关键认知：挖宝组队的流程很灵活
///
/// 1. 招募进组 → 跨服小队 → 聚集 → 转本服小队 → 挖宝
/// 2. 招募进组 → 聚集 → 解散 → 重新组本服小队 → 挖宝
/// 3. 先本服组队 → 再挂招募等人 → 挖宝
///
/// 所以「通过招募加入」不能作为必要条件。
/// 换队之后队伍可能还是同一批人在挖宝。
///
/// ══════════════════════════════════════════════════════════════
/// 正确的判定：看「行为证据」
///
/// 核心是「队伍在挖宝」，而不是「怎么组的队」。
/// 判定条件：
///   1. 在小队里（PartyList.Length &gt; 1）
///   2. 且 有挖宝行为证据（满足任一即可）：
///      a) 陷阱魔物全清       「将陷阱引来的魔物全部打倒了！」
///      b) 进入宝库副本        TerritoryType 命中宝库表
///      c) 出现挖宝小怪       洋葱/茄子/盗宝团等
///      d) 传送魔纹出现       「通往宝物库的传送魔纹出现了！」
///      e) 招募简介含挖宝特征  G18 12张图 / 深绿 5t
///   3. 证据在有效期内（默认 180 分钟）
///
/// 其中 (a)(b)(c)(d) 都是**硬证据** —— 只有真的在挖宝才会出现。
/// (e) 是软证据，用于「刚组好队还没开挖」的预热期。
///
/// 换队不影响：证据挂在时间窗上，不绑队伍实例。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed class TreasurePartyDetector
{
    private readonly IPluginLog log;
    private readonly IPartyList partyList;

    /// <summary>招募简介里的挖宝特征（软证据）。</summary>
    private static readonly Regex TreasureDescRegex = new(
        @"((G|g)\d{1,2}|深绿|绿图|挖宝|宝物库)(( |:|：|/|、|\\|\.|。|`|·|，|,|-|—|；|;|　){0,3}\d{1,2}(张|个|)(图|t|T|)|[^0-9]*挖|)",
        RegexOptions.Compiled);

    /// <summary>已知挖宝队常用讨伐名。</summary>
    private const string KnownDutyName = "讨伐歼灭战：那布里亚勒斯讨伐战";

    // ── 证据 ──

    /// <summary>最近一次硬证据的时间。</summary>
    public DateTime LastHardEvidence { get; private set; } = DateTime.MinValue;

    /// <summary>最近一次证据的说明。</summary>
    public string LastEvidence { get; private set; } = string.Empty;

    /// <summary>硬证据累计次数。</summary>
    public int HardEvidenceCount { get; private set; }

    // ── 软证据（招募信息）──

    public string JoinedTitle { get; private set; } = string.Empty;
    public string JoinedDescription { get; private set; } = string.Empty;
    public bool HasRecruitHint { get; private set; }
    public string RecruitHintReason { get; private set; } = string.Empty;

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

    // ── 记录证据 ──

    /// <summary>记录一次硬证据（打怪、进本、魔纹等）。</summary>
    public void MarkHardEvidence(string reason)
    {
        LastHardEvidence = DateTime.Now;
        LastEvidence = reason;
        HardEvidenceCount++;
        log.Debug("[TreasureLedger] 挖宝证据：{Reason}", reason);
    }

    /// <summary>记录招募信息（软证据）。</summary>
    public void OnRecruitInfo(string title, string description)
    {
        JoinedTitle = title ?? string.Empty;
        JoinedDescription = description ?? string.Empty;

        HasRecruitHint = JudgeRecruitHint(JoinedTitle, JoinedDescription, out var reason);
        RecruitHintReason = reason;

        log.Information(
            "[TreasureLedger] 招募信息：标题=「{Title}」简介含挖宝特征={Hint}（{Reason}）",
            JoinedTitle, HasRecruitHint, reason);
    }

    /// <summary>清除所有证据（新的一次挖宝开始时）。</summary>
    public void Clear()
    {
        LastHardEvidence = DateTime.MinValue;
        LastEvidence = string.Empty;
        HardEvidenceCount = 0;
        JoinedTitle = string.Empty;
        JoinedDescription = string.Empty;
        HasRecruitHint = false;
        RecruitHintReason = string.Empty;
    }

    /// <summary>
    /// 是否处于挖宝小队中。
    /// </summary>
    public bool IsTreasureParty(int windowMinutes)
    {
        // 必须在小队里
        if (!InParty) return false;

        var window = TimeSpan.FromMinutes(windowMinutes);

        // 硬证据优先：真的打过挖宝怪 / 进过宝库
        if (LastHardEvidence != DateTime.MinValue
            && DateTime.Now - LastHardEvidence <= window)
            return true;

        // 软证据：招募简介像挖宝队，但还没开挖
        // 只在「刚组队、还没产生行为证据」时作为预热
        if (HasRecruitHint) return true;

        return false;
    }

    /// <summary>判定依据说明，给 UI 显示。</summary>
    public string VerdictText(int windowMinutes)
    {
        if (!InParty) return "不在小队中";

        var window = TimeSpan.FromMinutes(windowMinutes);

        if (LastHardEvidence != DateTime.MinValue
            && DateTime.Now - LastHardEvidence <= window)
        {
            var ago = (int)(DateTime.Now - LastHardEvidence).TotalMinutes;
            return $"挖宝证据：{LastEvidence}（{ago} 分钟前）";
        }

        if (HasRecruitHint)
            return $"招募提示：{RecruitHintReason}（尚未产生行为证据）";

        return "没有挖宝证据";
    }

    /// <summary>
    /// 判定招募信息是否像挖宝队。
    /// 这个只看招募，不能作为唯一依据，只能当预热。
    /// </summary>
    public static bool JudgeRecruitHint(string title, string description, out string reason)
    {
        reason = string.Empty;

        var t = (title ?? string.Empty).Trim();
        var d = (description ?? string.Empty).Trim();

        // 标题以「寻宝」开头
        if (t.StartsWith("寻宝", StringComparison.Ordinal))
        {
            reason = "标题以「寻宝」开头";
            return true;
        }

        // 已知挖宝队常用讨伐名
        if (string.Equals(t, KnownDutyName, StringComparison.OrdinalIgnoreCase))
        {
            reason = "挖宝队常用讨伐名";
            return true;
        }

        // 简介含挖宝特征
        if (d.Length > 0 && TreasureDescRegex.IsMatch(d))
        {
            reason = "简介含挖宝特征";
            return true;
        }

        // 标题里带挖宝特征
        if (t.Length > 0 && TreasureDescRegex.IsMatch(t))
        {
            reason = "标题含挖宝特征";
            return true;
        }

        reason = "标题与简介都不像挖宝招募";
        return false;
    }
}
