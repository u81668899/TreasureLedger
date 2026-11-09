using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TreasureLedger.Maps;

/// <summary>
/// 文本模式识别。
///
/// 有些事件走的是「XX 出现了！」这种通用 LogMessageId（参数里只有名字），
/// 不同场景共用同一个 ID，无法只靠 ID 区分。
/// 所以这里对文本内容再做一层匹配，正则直接沿用 Triggernometry XML 里的写法。
/// </summary>
public static class TextPatterns
{
    /// <summary>下底 boss 出现。XML: g10/g12/g15/g18下底</summary>
    private static readonly Regex BottomBossRegex = new(
        @"(神殿曼提克|神殿变种龙|神殿阿班达|代恩·奥瑟|纳西索斯|法厄同|希波墨涅斯|雷电之魂|移动要塞|六足炮台)出现了",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>神殿召唤分级：上级/中级/下级体系。XML: 2.层数记录</summary>
    private static readonly Regex ShrineTierClassicRegex = new(
        @"(召唤失败|小梦怡情|中梦悦性|大梦安神|终梦修身|奇梦抚心)(，|！)",
        RegexOptions.Compiled);

    /// <summary>
    /// 神殿召唤分级：带上下级字样的通用写法。
    /// 「上级」「中级」「下级」出现在宝库召唤里。
    /// </summary>
    private static readonly Regex ShrineTierLevelRegex = new(
        @"(上级|中级|下级|大梦|终梦)",
        RegexOptions.Compiled);

    /// <summary>大门/闸区：打开了通往第X区的大门。XML: 6.记录层数</summary>
    private static readonly Regex GateRegex = new(
        @"(打开了通往|距)(第(?<floor>.+?)闸?区|(?<floor>最终区|最终闸区))(的大门！|被封锁还有..?秒。|的封锁解除了……)",
        RegexOptions.Compiled);

    /// <summary>门发光了。XML: 8.0门发光了！</summary>
    private static readonly Regex DoorGlowRegex = new(
        @"::(.+?)的门发光了|门发光了！",
        RegexOptions.Compiled);

    /// <summary>任务开始：「XX」任务开始。</summary>
    private static readonly Regex DutyStartRegex = new(
        @"“(?<name>.+?)”任务开始",
        RegexOptions.Compiled);

    /// <summary>任务结束：「XX」任务结束了。</summary>
    private static readonly Regex DutyEndRegex = new(
        @"“(?<name>.+?)”任务结束了",
        RegexOptions.Compiled);

    /// <summary>招募信息：招募(内容)(：|:| )(xxx)。XML: 4.6手动设置招募内容</summary>
    private static readonly Regex RecruitTextRegex = new(
        @"招募(内容|)(：|:| |)(?<text>.*)",
        RegexOptions.Compiled);

    // ── 查询方法 ──

    public static bool TryGetBottomBoss(string text, out string boss)
    {
        boss = string.Empty;
        var m = BottomBossRegex.Match(text);
        if (!m.Success) return false;
        boss = m.Groups[1].Value;
        return true;
    }

    /// <summary>识别神殿召唤分级。G17-G18 用大梦/终梦，其余用上中下级。</summary>
    public static bool TryGetShrineTier(string text, bool dreamNaming, out Ledger.ShrineTier tier)
    {
        tier = Ledger.ShrineTier.Middle;

        if (dreamNaming)
        {
            // 大梦 = 上级，终梦 = 下级，其余归中级
            if (text.Contains("大梦", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.High; return true; }
            if (text.Contains("终梦", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Low; return true; }
            if (text.Contains("中梦", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Middle; return true; }
            if (text.Contains("召唤失败", StringComparison.Ordinal)) return false;
            return false;
        }

        if (text.Contains("上级", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.High; return true; }
        if (text.Contains("中级", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Middle; return true; }
        if (text.Contains("下级", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Low; return true; }

        // XML 里的梦系命名
        if (text.Contains("大梦安神", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.High; return true; }
        if (text.Contains("中梦悦性", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Middle; return true; }
        if (text.Contains("小梦怡情", StringComparison.Ordinal)) { tier = Ledger.ShrineTier.Low; return true; }

        return false;
    }

    /// <summary>大门/闸区推进。返回区域名。</summary>
    public static bool TryGetGate(string text, out string area)
    {
        area = string.Empty;
        var m = GateRegex.Match(text);
        if (!m.Success) return false;
        area = m.Groups["floor"].Value;
        return true;
    }

    public static bool IsDoorGlow(string text) => DoorGlowRegex.IsMatch(text);

    public static bool TryGetDutyName(string text, out string name)
    {
        name = string.Empty;

        var m = DutyStartRegex.Match(text);
        if (m.Success) { name = m.Groups["name"].Value; return true; }

        m = DutyEndRegex.Match(text);
        if (m.Success) { name = m.Groups["name"].Value; return true; }

        return false;
    }
}
