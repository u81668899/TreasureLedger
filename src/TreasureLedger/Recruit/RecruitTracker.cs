using System;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Recruit;

/// <summary>
/// 招募内容记录。
///
/// 两个数据源（按需求验证后的结论）：
///
/// 1) PartyFinderGui.ReceiveListing —— 事件流，实时抓。
///    缺点：招募关闭后不再推送，最后一个进去的人可能抓不到。
///
/// 2) AgentLookingForGroup.LastComment —— 内存缓存，招募关闭也留着。
///    优点：进本后仍可读，补上事件流的缺口。
///    缺点：只在打开过招募板或收到过招募时才有值。
///
/// 3) 聊天日志兜底 —— XML 的做法，匹配「招募内容：xxx」这类文本。
///
/// 三者都试，谁先拿到用谁，拿到就缓存起来，查询时直接读缓存。
/// </summary>
public sealed class RecruitTracker : IDisposable
{
    private readonly IPluginLog log;
    private readonly IPartyFinderGui partyFinder;

    /// <summary>当前缓存的招募内容。</summary>
    public string Current { get; private set; } = string.Empty;

    /// <summary>缓存时间。</summary>
    public DateTime CachedAt { get; private set; } = DateTime.MinValue;

    /// <summary>招募内容变化。</summary>
    public event Action? Changed;

    /// <summary>从聊天日志兜底匹配：招募(内容)(：|:| )(xxx)</summary>
    private static readonly Regex ChatRecruitRegex = new(
        @"招募(内容|)(：|:| |)(?<text>.*)", RegexOptions.Compiled);

    public RecruitTracker(IPluginLog log, IPartyFinderGui partyFinder)
    {
        this.log = log;
        this.partyFinder = partyFinder;

        partyFinder.ReceiveListing += OnReceiveListing;
    }

    public void Dispose() => partyFinder.ReceiveListing -= OnReceiveListing;

    /// <summary>清理（换队/进本时调用）。</summary>
    public void Clear()
    {
        Current = string.Empty;
        CachedAt = DateTime.MinValue;
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

    /// <summary>从聊天文本兜底识别。</summary>
    public bool TryParseChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 只在我们关心的情况下识别，避免误抓普通聊天
        if (!text.Contains("招募", StringComparison.Ordinal)) return false;

        var m = ChatRecruitRegex.Match(text);
        if (!m.Success) return false;

        var content = m.Groups["text"].Value.Trim();
        if (content.Length == 0) return false;

        Set(content);
        return true;
    }

    private void OnReceiveListing(
        Dalamud.Game.Gui.PartyFinder.Types.IPartyFinderListing listing,
        Dalamud.Game.Gui.PartyFinder.Types.IPartyFinderListingEventArgs args)
    {
        try
        {
            var desc = listing.Description.TextValue ?? string.Empty;
            if (desc.Length == 0) return;

            // 记录最近一次看到的招募描述
            Set(desc);
        }
        catch (Exception ex)
        {
            log.Debug("[TreasureLedger] 读取招募列表失败：{Msg}", ex.Message);
        }
    }
}
