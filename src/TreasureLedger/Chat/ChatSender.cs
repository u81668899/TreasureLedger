using System;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Chat;

/// <summary>
/// 聊天发送器 —— 安全模式（只用 Dalamud 官方封装，永不碰原生指针）。
///
/// ══════════════════════════════════════════════════════════════
/// ⚠️ 崩溃事故记录（2026-11-09 22:55，不可重犯）
///
/// 曾经这样写过：
///     var module = RaptureLogModule.Instance();
///     var info = new LogInfo { Value = (ushort)kind };
///     module->PrintMessage(info, null, msg, Environment.TickCount, false);
///
/// 结果：游戏立即崩溃，异常代码 0xC0000005（访问冲突）。
/// 崩溃堆栈：
///     ffxiv_dx11.exe+88ECB
///     ILStubClass.IL_STUB_PInvoke(RaptureLogModule*, LogInfo, Utf8String*, ...)
///     Dalamud.Game.Gui.ChatGui.HandlePrintMessageDetour(...)
///
/// 根因：PrintMessage 的第二个参数 senderName 是 Utf8String*，
///       游戏内部会解引用它取发送者名字。传 null = 解引用空指针 = 秒崩。
///
/// 教训：
///   1. 原生指针参数传 null 之前，必须确认游戏内部会不会解引用。
///   2. 结构体（LogInfo）和字符串（Utf8String）的生命周期、所有权要先搞清楚。
///   3. 未经验证的原生调用绝不能直接部署到用户正在玩的游戏里。
///   4. 要真实发频道消息，应该复用 Dalamud 已有的安全封装，
///      而不是自己拼 LogInfo + PrintMessage。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed class ChatSender : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;

    public ChatSender(IChatGui chat, IPluginLog log)
    {
        this.chat = chat;
        this.log = log;
    }

    public void Dispose() { }

    /// <summary>本地输出（安全）。</summary>
    public void PrintLocal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            chat.Print(text);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] Print 失败");
        }
    }

    /// <summary>
    /// 输出到指定频道。
    /// 当前实现只做本地输出并加频道前缀标记 —— 真正的频道发送待验证方案。
    /// </summary>
    public bool SendTo(ReportChannel channel, Configuration config, string text)
    {
        var tag = Configuration.ChannelName(channel);
        PrintLocal($"[{tag}] {text}");
        return true;
    }

    public bool Send(XivChatType kind, string text)
    {
        PrintLocal(text);
        return true;
    }
}
