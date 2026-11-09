using System;
using System.Linq;
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
    /// 发到指定频道。一个频道可能对应多个目标
    /// （比如同时挂在 2 个通讯贝里，就都发一份）。
    /// </summary>
    /// <param name="tellTarget">悄悄话收件人，只在 Tell 频道用到。</param>
    public bool SendTo(ReportChannel channel, Configuration config, string text, string? tellTarget = null)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (channel == ReportChannel.Tell)
            return SendTell(config, text, tellTarget);

        var kinds = ResolveKinds(channel, config);
        var ok = false;

        foreach (var kind in kinds)
            ok |= Send(kind, text);

        return ok;
    }

    /// <summary>
    /// 悄悄话：发给每个指定对象。
    /// 没指定对象时回给发消息给我们的人。
    /// </summary>
    private bool SendTell(Configuration config, string text, string? tellTarget)
    {
        // 目标是「固定名单」优先，其次回给发件人
        var targets = config.TellTargets
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        if (targets.Count == 0 && !string.IsNullOrWhiteSpace(tellTarget))
            targets.Add(tellTarget.Trim());

        if (targets.Count == 0)
        {
            log.Warning("[TreasureLedger] 悄悄话缺少收件人，已跳过");
            return false;
        }

        var ok = false;
        foreach (var t in targets)
        {
            if (PreviewOnly)
            {
                PrintLocal($"[悄悄话→{t}] {text}");
                ok = true;
                continue;
            }

            ok |= Send(XivChatType.TellOutgoing, text, tellTarget: t);
        }

        return ok;
    }

    /// <summary>
    /// 用官方封装发送。XivChatEntry 的 Type 指定频道。
    ///
    /// 依据 Dalamud 源码 ChatGui.UpdateQueue：
    ///     var targetChannel = chat.Type ?? this.configuration.GeneralChatType;
    ///     HandlePrintMessageDetour(..., new LogInfo { LogKind = (ushort)targetChannel }, ...)
    /// 所以只要 Type 不为 null，频道就由我们决定。
    ///
    /// 悄悄话需要 Name 填收件人名（游戏用它决定发给谁）。
    /// </summary>
    public bool Send(XivChatType kind, string text, string? displayName = null, string? tellTarget = null)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 悄悄话必须有收件人，否则不发，避免变成谁都能看到的广播
        if (kind is XivChatType.TellOutgoing or XivChatType.TellIncoming
            && string.IsNullOrWhiteSpace(tellTarget))
        {
            log.Warning("[TreasureLedger] 悄悄话缺少收件人，已跳过");
            return false;
        }

        try
        {
            var entry = new XivChatEntry
            {
                Type = kind,
                Message = text,
                Name = tellTarget ?? string.Empty,
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

    /// <summary>一个频道可能对应多个实际聊天类型（多个贝）。</summary>
    private static XivChatType[] ResolveKinds(ReportChannel channel, Configuration config) => channel switch
    {
        ReportChannel.Echo => new[] { XivChatType.Echo },
        ReportChannel.Party => new[] { XivChatType.Party },
        ReportChannel.LinkshellLocal => config.LocalLinkshellIndices
            .Where(i => i is >= 1 and <= 8)
            .DefaultIfEmpty(1)
            .Select(LocalLsType)
            .ToArray(),
        ReportChannel.CrossWorldLinkshell => config.CrossLinkshellIndices
            .Where(i => i is >= 1 and <= 8)
            .DefaultIfEmpty(1)
            .Select(CrossLsType)
            .ToArray(),
        ReportChannel.Tell => new[] { XivChatType.TellOutgoing },
        _ => new[] { XivChatType.Echo },
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
