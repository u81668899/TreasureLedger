using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Recruit;

/// <summary>
/// 招募内容记录。
///
/// 重要约束（按需求）：
/// 只记录「我进了的那支队伍」的招募内容，不是招募板上任意一条。
/// 招募板上可能同时有好几个挖宝队，抓错了就会显示别人的招募。
///
/// 判定「我进队」的信号：
/// 1. 聊天日志「加入了XX组建的"YYY"(队伍|队员招募用的团队)」 —— 最直接
/// 2. 「开始招募队员」 —— 自己开的招募
/// 3. 加入小队 / 加入通讯贝 —— 兜底
///
/// 数据源优先级：
/// a) 聊天日志里的招募文本（XML 做法），最贴近「我这队」
/// b) 进队那一刻用 LastComment 补，避免日志没带内容
/// c) 招募板事件只用来「预缓存」，不会直接当作我的招募
/// </summary>
public sealed class RecruitTracker : IDisposable
{
    private readonly IPluginLog log;
    private readonly IPartyFinderGui partyFinder;

    /// <summary>当前我这队的招募内容。</summary>
    public string Current { get; private set; } = string.Empty;

    public DateTime CachedAt { get; private set; } = DateTime.MinValue;

    /// <summary>是否真的已经进队（没进队时不该显示招募）。</summary>
    public bool Joined { get; private set; }

    public event Action? Changed;

    /// <summary>通过招募加入队伍时触发：（标题, 简介）。</summary>
    public event Action<string, string>? JoinedRecruit;

    /// <summary>「招募内容：xxx」这类直接设置。</summary>
    private static readonly Regex RecruitTextRegex = new(
        @"招募(内容|)(：|:| |)(?<text>.*)", RegexOptions.Compiled);

    /// <summary>「加入了XX组建的"YYY"」——进队信号，YYY 通常是招募名。</summary>
    private static readonly Regex JoinedPartyRegex = new(
        @"加入了(?<name>.{1,10})组建的“(?<title>.*)”(队伍|队员招募用的团队)",
        RegexOptions.Compiled);

    /// <summary>「加入了XX的小队」</summary>
    private static readonly Regex JoinedSmallPartyRegex = new(
        @"加入了(?<name>.{1,10})的小队", RegexOptions.Compiled);

    /// <summary>自己开招募</summary>
    private static readonly Regex RecruitStartedRegex = new(
        @"(开始招募队员|已重新开始招募队员)", RegexOptions.Compiled);

    /// <summary>招募板事件里暂存的候选，仅用于进队后补内容，不会直接显示。</summary>
    private string pendingBoardText = string.Empty;

    public RecruitTracker(IPluginLog log, IPartyFinderGui partyFinder)
    {
        this.log = log;
        this.partyFinder = partyFinder;

        partyFinder.ReceiveListing += OnReceiveListing;
    }

    public void Dispose() => partyFinder.ReceiveListing -= OnReceiveListing;

    /// <summary>离开队伍 / 换队时清理。</summary>
    public void Clear()
    {
        Current = string.Empty;
        CachedAt = DateTime.MinValue;
        Joined = false;
        pendingBoardText = string.Empty;
        Changed?.Invoke();
    }

    /// <summary>手动设置（UI 用）。</summary>
    public void Set(string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t == Current) return;

        Current = t;
        CachedAt = DateTime.Now;
        log.Information("[TreasureLedger] 招募内容：{Text}", t);
        Changed?.Invoke();
    }

    /// <summary>
    /// 处理聊天日志。
    /// 返回 true 表示这条日志影响了招募状态，可中断后续处理。
    /// </summary>
    public bool TryParseChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 1) 进队信号：加入了某人的招募
        var m = JoinedPartyRegex.Match(text);
        if (m.Success)
        {
            Joined = true;

            var title = m.Groups["title"].Value.Trim();

            // 优先用日志里的招募名；没有就用招募板暂存的内容
            if (title.Length > 0 && !IsGenericTitle(title))
            {
                Set(title);
            }
            else if (pendingBoardText.Length > 0)
            {
                Set(pendingBoardText);
            }
            else
            {
                Changed?.Invoke();
            }

            log.Information("[TreasureLedger] 检测进队，招募名={Title}", title);

            // 抛给队伍判定器
            var desc = Current.Length > 0 ? Current : pendingBoardText;
            JoinedRecruit?.Invoke(title, desc);

            return true;
        }

        // 2) 加入小队
        if (JoinedSmallPartyRegex.IsMatch(text))
        {
            Joined = true;
            if (Current.Length == 0 && pendingBoardText.Length > 0)
                Set(pendingBoardText);
            else
                Changed?.Invoke();
            return true;
        }

        // 3) 自己开招募
        if (RecruitStartedRegex.IsMatch(text))
        {
            Joined = true;
            Changed?.Invoke();
            return true;
        }

        // 4) 显式设置招募内容（XML 的做法）
        if (text.Contains("招募", StringComparison.Ordinal))
        {
            var rm = RecruitTextRegex.Match(text);
            if (rm.Success)
            {
                var content = rm.Groups["text"].Value.Trim();
                if (content.Length > 0)
                {
                    Set(content);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>过滤掉「挖宝」「招募」这种没信息的通用名字。</summary>
    private static bool IsGenericTitle(string title)
        => title.Length <= 2
           || title.Equals("招募", StringComparison.Ordinal)
           || title.Equals("挖宝", StringComparison.Ordinal)
           || title.Equals("组队", StringComparison.Ordinal);

    /// <summary>
    /// 招募板事件：只做暂存，不作为「我的招募」。
    /// 因为板子上可能有好几个队，进队时才知道是哪个。
    /// </summary>
    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        try
        {
            var desc = listing.Description.TextValue ?? string.Empty;
            if (desc.Length == 0) return;

            // 只暂存看起来像挖宝招募的
            if (!desc.Contains("挖宝", StringComparison.Ordinal)
                && !desc.Contains("g1", StringComparison.OrdinalIgnoreCase)
                && !desc.Contains("图", StringComparison.Ordinal)
                && !desc.Contains("底", StringComparison.Ordinal))
                return;

            pendingBoardText = desc;
        }
        catch (Exception ex)
        {
            log.Debug("[TreasureLedger] 读取招募列表失败：{Msg}", ex.Message);
        }
    }
}
