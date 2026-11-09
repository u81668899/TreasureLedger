using System;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace TreasureLedger.Chat;

/// <summary>
/// 游戏原生命令执行器。
///
/// 这是正确执行 /dutyfinder、/退出副本 这类游戏内建指令的方式。
/// 之前用 CommandManager.ProcessCommand 是错的 ——
/// 那个只能执行插件自己注册的斜杠指令，对游戏原生命令一律返回 false。
///
/// 实现参考 ECommons 的 Chat.SendMessageUnsafe：
///   1. 特征码定位 ProcessChatBox 函数
///      "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9"
///   2. 构造 ChatPayload（textPtr / unk1 / textLen / unk2 四个 8 字节字段）
///   3. ProcessChatBox(UIModule, &payload, 0, 0)
///
/// ⚠️ 这是原生调用，必须全程 try-catch，任何失败只记日志不抛异常。
/// </summary>
public sealed unsafe class GameCommandExecutor : IDisposable
{
    private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unk1, int unk2);

    private const string ProcessChatBoxSignature =
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";

    private readonly IPluginLog log;
    private readonly ISigScanner sigScanner;

    private ProcessChatBoxDelegate? processChatBox;

    public GameCommandExecutor(IPluginLog log, ISigScanner sigScanner)
    {
        this.log = log;
        this.sigScanner = sigScanner;
    }

    public void Dispose() => processChatBox = null;

    /// <summary>函数是否已定位。</summary>
    public bool Ready => processChatBox is not null;

    /// <summary>最近一次执行结果，给 UI 显示。</summary>
    public string LastResult { get; private set; } = string.Empty;

    /// <summary>累计执行次数。</summary>
    public int ExecutedCount { get; private set; }

    /// <summary>定位 ProcessChatBox。</summary>
    public bool TryInitialize()
    {
        if (processChatBox is not null) return true;

        try
        {
            var addr = sigScanner.ScanText(ProcessChatBoxSignature);
            if (addr == IntPtr.Zero)
            {
                log.Warning("[TreasureLedger] 未找到 ProcessChatBox 特征码");
                return false;
            }

            processChatBox = Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(addr);
            log.Information("[TreasureLedger] ProcessChatBox 已定位 @ {Addr:X}", addr.ToInt64());
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 定位 ProcessChatBox 失败");
            processChatBox = null;
            return false;
        }
    }

    /// <summary>
    /// 执行一条游戏原生命令，必须以 / 开头。
    /// 返回 true 表示已经调用了函数（不代表游戏一定接受了这条命令）。
    /// </summary>
    public bool Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            LastResult = "命令为空";
            return false;
        }

        command = command.Trim();
        if (!command.StartsWith('/'))
        {
            LastResult = "命令必须以 / 开头";
            log.Warning("[TreasureLedger] 拒绝执行非斜杠命令：{Cmd}", command);
            return false;
        }

        if (!TryInitialize())
        {
            LastResult = "ProcessChatBox 未就绪";
            return false;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(command);
            if (bytes.Length == 0 || bytes.Length > 500)
            {
                LastResult = "命令长度不合法";
                return false;
            }

            var uiModule = GetUIModuleAddress();
            if (uiModule == IntPtr.Zero)
            {
                LastResult = "UIModule 为空";
                return false;
            }

            SendBytes(uiModule, bytes);

            ExecutedCount++;
            LastResult = "已执行：" + command;
            log.Information("[TreasureLedger] 执行原生命令：{Cmd}", command);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 执行原生命令失败：{Cmd}", command);
            LastResult = "异常：" + ex.Message;
            return false;
        }
    }

    /// <summary>拿 UIModule 指针。</summary>
    private static IntPtr GetUIModuleAddress()
    {
        try
        {
            var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
            if (framework is null) return IntPtr.Zero;

            var uiModule = framework->GetUIModule();
            return (IntPtr)uiModule;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 调用 ProcessChatBox。
    /// 载荷布局（4 个 8 字节字段）：
    ///   offset 0  : textPtr  — 指向 UTF-8 字符串
    ///   offset 8  : unk1     — 固定 64
    ///   offset 16 : textLen  — 字符串长度 + 1
    ///   offset 24 : unk2     — 固定 0
    /// </summary>
    private void SendBytes(IntPtr uiModule, byte[] bytes)
    {
        var textPtr = Marshal.AllocHGlobal(bytes.Length + 30);
        var payloadPtr = Marshal.AllocHGlobal(32);

        try
        {
            Marshal.Copy(bytes, 0, textPtr, bytes.Length);
            Marshal.WriteByte(textPtr + bytes.Length, 0);

            Marshal.WriteInt64(payloadPtr, 0, textPtr.ToInt64());
            Marshal.WriteInt64(payloadPtr, 8, 64);
            Marshal.WriteInt64(payloadPtr, 16, bytes.Length + 1);
            Marshal.WriteInt64(payloadPtr, 24, 0);

            processChatBox!(uiModule, payloadPtr, IntPtr.Zero, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(payloadPtr);
            Marshal.FreeHGlobal(textPtr);
        }
    }
}
