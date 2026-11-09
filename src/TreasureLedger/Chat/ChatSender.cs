using System;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Chat;

/// <summary>
/// 聊天发送器。
///
/// ══════════════════════════════════════════════════════════════
/// ⚠️ 崩溃事故记录（2026-11-09 22:55，不可重犯）
///
/// 曾经这样写过：
///     var module = RaptureLogModule.Instance();
///     module->PrintMessage(info, null, msg, Environment.TickCount, false);
///
/// 结果：游戏立即崩溃，异常代码 0xC0000005（访问冲突）。
/// 崩溃堆栈：
///     ffxiv_dx11.exe+88ECB
///     ILStubClass.IL_STUB_PInvoke(RaptureLogModule*, LogInfo, Utf8String*, ...)
///     Dalamud.Game.Gui.ChatGui.HandlePrintMessageDetour(...)
///
/// 根因：PrintMessage 的 senderName 是 Utf8String*，游戏内部会解引用它。
///       传 null = 解引用空指针 = 秒崩。
///
/// 教训：
///   1. 原生指针参数传 null 前，必须确认游戏内部是否会解引用。
///   2. 未经验证的原生调用绝不能直接部署到正在玩的游戏里。
///   3. 优先使用 Dalamud 官方封装（IChatGui.Print(XivChatEntry)），
///      它内部会正确处理 sender 与 Utf8String 生命周期。
/// ══════════════════════════════════════════════════════════════
///
/// 当前实现：
/// - 默认走 IChatGui.Print(XivChatEntry)，用 Type 指定频道（官方安全封装）
/// - 若 PreviewOnly = true，则只本地回显，不真发（默认开启，等验证后再关）
/// </summary>
public sealed class ChatSender : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;

    /// <summary>
    /// 仅预览模式：只本地回显，不真发到频道。
    /// 等用户验证 Print(XivChatEntry) 不崩后再关闭。
    /// </summary>
    public bool PreviewOnly { get; set; } = true;

    public ChatSender(IChatGui chat, IPluginLog log)
    {
        this.chat = chat;
        this.log = log;
    }

    public void Dispose() { }

    /// <summary>本地回显（安全，仅测试用）。</summary>
    public void PrintLocal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { chat.Print(text); }
        catch (Exception ex) { log.Error(ex, "[TreasureLedger] Print 失败"); }
    }

    /// <summary>
    /// 发到指定频道。
    /// 预览模式下只本地回显并加频道前缀。
    /// </summary>
    public bool SendTo(ReportChannel channel, Configuration config, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var kind = ResolveKind(channel, config);

        if (PreviewOnly)
        {
            PrintLocal($"[{Configuration.ChannelName(channel)}] {text}");
            return true;
        }

        return Send(kind, text, displayName: Configuration.ChannelName(channel));
    }

    /// <summary>
    /// 用官方封装发送。XivChatEntry 的 Type 指定频道，
    /// Dalamud 内部会处理 sender 与 Utf8String，不碰原生指针。
    /// </summary>
    public bool Send(XivChatType kind, string text, string? displayName = null)
    {
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            var entry = new XivChatEntry
            {
                Type = kind,
                Message = text,
                Name = string.Empty,
                Silent = false,
                Timestamp = Environment.TickCount,
            };

            chat.Print(entry);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] Print(XivChatEntry) 失败（{Kind}）", kind);
            return false;
        }
    }

    private static XivChatType ResolveKind(ReportChannel channel, Configuration config) => channel switch
    {
        ReportChannel.Echo => XivChatType.Echo,
        ReportChannel.Party => XivChatType.Party,
        ReportChannel.LinkshellLocal => LocalLsType(config.LocalLinkshellIndex),
        ReportChannel.CrossWorldLinkshell => CrossLsType(config.CrossLinkshellIndex),
        _ => XivChatType.Echo,
    };

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
