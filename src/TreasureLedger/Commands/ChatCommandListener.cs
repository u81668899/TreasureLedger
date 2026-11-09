using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using TreasureLedger.Chat;
using TreasureLedger.Ledger;
using TreasureLedger.Report;

namespace TreasureLedger.Commands;

/// <summary>
/// 聊天指令监听：从聊天频道里的文本识别自定义指令。
/// 谁在哪个频道发，就往哪个频道回。
/// </summary>
public sealed class ChatCommandListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly ChatSender sender;
    private readonly Action resetLedger;

    public ChatCommandListener(
        IChatGui chat,
        IPluginLog log,
        Configuration config,
        SessionLedger ledger,
        ChatSender sender,
        Action resetLedger)
    {
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.ledger = ledger;
        this.sender = sender;
        this.resetLedger = resetLedger;

        chat.ChatMessage += OnChatMessage;
    }

    public void Dispose() => chat.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var channel = MapChannel(message.LogKind);
            if (channel is null) return;

            if (!config.IsListenChannel(channel.Value)) return;

            var text = message.Message.TextValue?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (Matches(text!, config.QueryCommand))
            {
                log.Information("[TreasureLedger] 查询请求（{Ch}）", Configuration.ChannelName(channel.Value));
                SendReport(channel.Value);
            }
            else if (Matches(text!, config.ResetCommand))
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

    private static bool Matches(string text, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return string.Equals(text.Trim(), command.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static ReportChannel? MapChannel(XivChatType kind) => kind switch
    {
        XivChatType.Echo => ReportChannel.Echo,
        XivChatType.Party => ReportChannel.Party,

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
        foreach (var line in ReportFormatter.BuildFullReport(ledger, config))
            sender.SendTo(channel, config, line);
    }

    private void SendLine(ReportChannel channel, string line)
        => sender.SendTo(channel, config, line);
}
