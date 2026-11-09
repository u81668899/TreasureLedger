using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using TreasureLedger.Items;
using TreasureLedger.Ledger;
using TreasureLedger.Report;

namespace TreasureLedger.Commands;

/// <summary>
/// 聊天指令监听：从聊天频道里的文本识别「挖宝查询」「挖宝重置」。
/// 查询来自哪个频道，就往哪个频道回。
/// </summary>
public sealed class ChatCommandListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly Action resetLedger;

    public ChatCommandListener(
        IChatGui chat,
        ICommandManager commands,
        IPluginLog log,
        Configuration config,
        SessionLedger ledger,
        Action resetLedger)
    {
        this.chat = chat;
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.ledger = ledger;
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

            // 玩家没开这个频道，不响应
            if (!config.IsListenChannel(channel.Value)) return;

            var text = message.Message.TextValue?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (Matches(text!, config.QueryCommand))
            {
                log.Information("[TreasureLedger] 收到查询请求（{Channel}）", Configuration.ChannelName(channel.Value));
                SendReport(channel.Value);
            }
            else if (Matches(text!, config.ResetCommand))
            {
                resetLedger();
                SendLine(channel.Value, "已重置本次挖宝统计。");
                log.Information("[TreasureLedger] 收到重置请求（{Channel}）", Configuration.ChannelName(channel.Value));
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 处理聊天指令失败");
        }
    }

    /// <summary>文本是否匹配指令（忽略大小写，允许前后空格）。</summary>
    private static bool Matches(string text, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return string.Equals(text.Trim(), command.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>XivChatType → 我们关心的频道。</summary>
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

    /// <summary>往指定频道发统计。</summary>
    public void SendReport(ReportChannel channel)
    {
        foreach (var line in ReportFormatter.BuildFullReport(ledger, config))
            SendLine(channel, line);
    }

    private void SendLine(ReportChannel channel, string line)
    {
        commands.ProcessCommand(config.ChannelSlash(channel) + line);
    }
}
