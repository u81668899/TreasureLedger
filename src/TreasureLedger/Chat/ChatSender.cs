using System;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace TreasureLedger.Chat;

/// <summary>
/// 频道发送器。
///
/// 实测结论：
/// - CommandManager.ProcessCommand("/e xxx") 返回 false，发不出去
/// - RaptureLogModule.PrintString 可用，但走系统频道
/// - 要指定频道必须走 PrintMessage(LogInfo, Utf8String* sender, Utf8String* message, ...)
/// </summary>
public sealed unsafe class ChatSender : IDisposable
{
    private readonly IPluginLog log;

    public ChatSender(IPluginLog log)
    {
        this.log = log;
    }

    public void Dispose() { }

    /// <summary>往指定聊天类型发一条文本。</summary>
    public bool Send(XivChatType kind, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var module = RaptureLogModule.Instance();
        if (module is null)
        {
            log.Warning("[TreasureLedger] RaptureLogModule 为空");
            return false;
        }

        Utf8String* msg = null;
        try
        {
            msg = Utf8String.FromString(text);
            if (msg is null) return false;

            var info = new LogInfo { Value = (ushort)kind };

            module->PrintMessage(info, null, msg, Environment.TickCount, false);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] PrintMessage 失败（{Kind}）", kind);
            return false;
        }
        finally
        {
            if (msg is not null) msg->Dtor();
        }
    }

    /// <summary>兜底：本地输出。</summary>
    public void PrintLocal(string text)
    {
        try
        {
            var module = RaptureLogModule.Instance();
            module->PrintString(text);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] PrintString 失败");
        }
    }

    public bool SendTo(ReportChannel channel, Configuration config, string text)
    {
        var kind = channel switch
        {
            ReportChannel.Echo => XivChatType.Echo,
            ReportChannel.Party => XivChatType.Party,
            ReportChannel.LinkshellLocal => LocalLsType(config.LocalLinkshellIndex),
            ReportChannel.CrossWorldLinkshell => CrossLsType(config.CrossLinkshellIndex),
            _ => XivChatType.Echo,
        };

        return Send(kind, text);
    }

    private static XivChatType LocalLsType(int index) => index switch
    {
        1 => XivChatType.Ls1,
        2 => XivChatType.Ls2,
        3 => XivChatType.Ls3,
        4 => XivChatType.Ls4,
        5 => XivChatType.Ls5,
        6 => XivChatType.Ls6,
        7 => XivChatType.Ls7,
        _ => XivChatType.Ls8,
    };

    private static XivChatType CrossLsType(int index) => index switch
    {
        1 => XivChatType.CrossLinkShell1,
        2 => XivChatType.CrossLinkShell2,
        3 => XivChatType.CrossLinkShell3,
        4 => XivChatType.CrossLinkShell4,
        5 => XivChatType.CrossLinkShell5,
        6 => XivChatType.CrossLinkShell6,
        7 => XivChatType.CrossLinkShell7,
        _ => XivChatType.CrossLinkShell8,
    };
}
