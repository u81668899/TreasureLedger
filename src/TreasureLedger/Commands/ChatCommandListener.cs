using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using TreasureLedger.Chat;
using TreasureLedger.Items;
using TreasureLedger.Ledger;
using TreasureLedger.Report;

namespace TreasureLedger.Commands;

/// <summary>指令匹配结果，用于 UI 显示底色。</summary>
public enum CommandMatchState
{
    /// <summary>空</summary>
    Empty,

    /// <summary>正则语法错误</summary>
    Invalid,

    /// <summary>合法</summary>
    Valid,
}

/// <summary>
/// 聊天指令监听：从聊天频道里的文本识别自定义指令。
/// 谁在哪个频道发，就往哪个频道回。
/// 支持纯文本匹配和正则匹配两种模式。
/// </summary>
public sealed class ChatCommandListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly ChatSender sender;
    private readonly Action resetLedger;
    private readonly Func<string> recruitProvider;
    private readonly ItemCatalog? catalog;
    private readonly Func<string>? mobSummaryProvider;
    private readonly IObjectTable? objectTable;

    public ChatCommandListener(
        IChatGui chat,
        IPluginLog log,
        Configuration config,
        SessionLedger ledger,
        ChatSender sender,
        Action resetLedger,
        Func<string> recruitProvider,
        ItemCatalog? catalog = null,
        Func<string>? mobSummaryProvider = null,
        IObjectTable? objectTable = null)
    {
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.ledger = ledger;
        this.sender = sender;
        this.resetLedger = resetLedger;
        this.recruitProvider = recruitProvider;
        this.catalog = catalog;
        this.mobSummaryProvider = mobSummaryProvider;
        this.objectTable = objectTable;

        chat.ChatMessage += OnChatMessage;
    }

    public void Dispose() => chat.ChatMessage -= OnChatMessage;

    /// <summary>缓存自己的角色名。</summary>
    private string selfName = string.Empty;

    /// <summary>取自己的角色名（缓存）。</summary>
    private string GetSelfName()
    {
        if (selfName.Length > 0) return selfName;

        try
        {
            var me = objectTable?.LocalPlayer;
            if (me is not null)
                selfName = me.Name.TextValue ?? string.Empty;
        }
        catch { }

        return selfName;
    }

    /// <summary>悄悄话回给谁：最近一次发消息给我们的人。</summary>
    public string LastTellSender { get; private set; } = string.Empty;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var channel = MapChannel(message.LogKind);
            if (channel is null) return;

            if (!config.IsListenChannel(channel.Value)) return;

            var text = message.Message.TextValue?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // 悄悄话：记住是谁发的，回消息要用。
            // 同时屏蔽自己 —— 自己发出去的悄悄话（TellOutgoing）不该被当成请求，
            // 既避免自问自答，也避免无限循环。
            if (channel.Value == ReportChannel.Tell)
            {
                var senderName = message.Sender.TextValue?.Trim();
                if (string.IsNullOrEmpty(senderName)) return;

                // 自己发的直接忽略
                var self = GetSelfName();
                if (self.Length > 0
                    && string.Equals(senderName, self, StringComparison.OrdinalIgnoreCase))
                    return;

                LastTellSender = senderName;
            }

            if (Matches(text!, config.QueryCommand, config.QueryCommandIsRegex))
            {
                log.Information("[TreasureLedger] 查询请求（{Ch}）", Configuration.ChannelName(channel.Value));
                SendReport(channel.Value);
            }
            else if (Matches(text!, config.ResetCommand, config.ResetCommandIsRegex))
            {
                resetLedger();
                SendLine(channel.Value, "已重置本次挖宝统计。");
                log.Information("[TreasureLedger] 重置请求（{Ch}）", Configuration.ChannelName(channel.Value));
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 处理聊天指令失败");
        }
    }

    /// <summary>按配置决定纯文本还是正则匹配。</summary>
    private static bool Matches(string text, string command, bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var input = text.Trim();
        var pattern = command.Trim();

        if (!useRegex)
            return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);

        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            // 正则写错了就当不匹配，不抛异常
            return false;
        }
    }

    /// <summary>检查一个表达式是不是合法正则（UI 用来显示红/绿底色）。</summary>
    public static CommandMatchState CheckPattern(string pattern, bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return CommandMatchState.Empty;
        if (!useRegex) return CommandMatchState.Valid;

        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
            return CommandMatchState.Valid;
        }
        catch (ArgumentException)
        {
            return CommandMatchState.Invalid;
        }
    }

    private static ReportChannel? MapChannel(XivChatType kind) => kind switch
    {
        XivChatType.Echo => ReportChannel.Echo,
        XivChatType.Party => ReportChannel.Party,

        // 只处理收到的悄悄话。
        // 不能把 TellOutgoing 也映射进来 —— 我们自己发出去的回复
        // 会再次被监听，如果内容里又含指令就会无限循环。
        XivChatType.TellIncoming => ReportChannel.Tell,

        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
            => ReportChannel.LinkshellLocal,

        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2
            or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4
            or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
            or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
            => ReportChannel.CrossWorldLinkshell,

        _ => null,
    };

    public void SendReport(ReportChannel channel)
    {
        foreach (var line in ReportFormatter.BuildFullReport(ledger, config, recruitProvider(), catalog, mobSummaryProvider?.Invoke()))
            sender.SendTo(channel, config, line, LastTellSender);
    }

    private void SendLine(ReportChannel channel, string line)
        => sender.SendTo(channel, config, line, LastTellSender);
}
