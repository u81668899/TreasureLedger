using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace TreasureLedger.Maps;

/// <summary>
/// 退出副本。
///
/// ══════════════════════════════════════════════════════════════
/// 实现来源与依赖关系
///
/// 反编译 DailyRoutines 的 LeaveDutyCommand 得到调用链：
///     DailyRoutines.LeaveDutyCommand
///       → OmenTools.DutyCommand.Leave(kind)
///         → ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.LeaveDuty, kind)
///           → FFXIVClientStructs.GameMain.ExecuteCommand   ← 最终落点
///
/// 关键：最终调用的是 FFXIVClientStructs 的静态方法，
/// 而 FFXIVClientStructs 是 Dalamud 自带的依赖，不是 DailyRoutines 的。
/// 所以本插件对 DailyRoutines / OmenTools 是【零依赖】的，
/// 关掉 DR 插件完全不影响这里。
/// ══════════════════════════════════════════════════════════════
///
/// 命令号：
///   command = 819  (ExecuteCommandFlag.LeaveDuty)
///   param1  = 0 = Normal（正常退本）
///             1 = Inactive（以「不活跃」方式退本）
///
/// ══════════════════════════════════════════════════════════════
/// 维护策略与失败保护
///
/// CS 内部用特征码定位这个函数：
///     "E8 ?? ?? ?? ?? 66 41 89 B7 ?? ?? ?? ?? ?? ?? ??"
///
/// 游戏大版本更新后，如果 CS 还没跟上，特征码会失配，
/// 此时 CS 内部的函数地址会是 0，直接调用会崩溃（访问违例）。
///
/// 所以这里做【预检 + 降级】：
///   1. 启动时用 ISigScanner 自己扫一遍同一特征码
///   2. 扫不到 → 判定为「CS 未更新」，禁用退本，只在 UI 提示
///   3. 扫得到 → 才允许调用 CS 的方法
///
/// 这样即使 CS 滞后，插件也只会「退本不可用」，
/// 而不是让游戏崩掉。
/// ══════════════════════════════════════════════════════════════
/// </summary>
public sealed class DutyExiter : IDisposable
{
    /// <summary>ExecuteCommandFlag.LeaveDuty</summary>
    private const int CommandLeaveDuty = 819;

    /// <summary>正常退出。</summary>
    private const int KindNormal = 0;

    /// <summary>不活跃退出。</summary>
    private const int KindInactive = 1;

    /// <summary>
    /// 用来预检的特征码。取自 FFXIVClientStructs 对
    /// GameMain.ExecuteCommand 的签名定义。
    /// 只用于判断「当前游戏版本还能不能解析到函数」，不用于直接调用。
    /// </summary>
    private const string ProbeSignature =
        "E8 ?? ?? ?? ?? 66 41 89 B7 ?? ?? ?? ?? ?? ?? ??";

    private readonly IPluginLog log;
    private readonly ISigScanner sigScanner;

    /// <summary>预检结果：当前版本能否安全调用退本。</summary>
    public bool Available { get; private set; }

    /// <summary>预检失败原因。</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    public string LastResult { get; private set; } = string.Empty;

    public int LeaveCount { get; private set; }

    public DutyExiter(IPluginLog log, ISigScanner sigScanner)
    {
        this.log = log;
        this.sigScanner = sigScanner;

        Probe();
    }

    public void Dispose() { }

    /// <summary>启动时预检：确认当前游戏版本还能解析到 ExecuteCommand。</summary>
    private void Probe()
    {
        try
        {
            var addr = sigScanner.ScanText(ProbeSignature);

            if (addr == IntPtr.Zero)
            {
                Available = false;
                UnavailableReason =
                    "未能解析 ExecuteCommand 特征码，可能是游戏已更新而 FFXIVClientStructs 尚未跟进。退本功能暂时不可用。";
                log.Warning("[TreasureLedger] 退本预检失败：特征码未匹配，已禁用退本");
                return;
            }

            Available = true;
            UnavailableReason = string.Empty;
            log.Information(
                "[TreasureLedger] 退本预检通过，ExecuteCommand 特征码已解析 @ {Addr:X}",
                addr.ToInt64());
        }
        catch (Exception ex)
        {
            Available = false;
            UnavailableReason = "退本预检异常：" + ex.Message;
            log.Error(ex, "[TreasureLedger] 退本预检异常，已禁用退本");
        }
    }

    /// <summary>重新预检（游戏版本切换后可在 UI 手动触发）。</summary>
    public void Recheck() => Probe();

    /// <summary>
    /// 请求退出副本。
    /// 返回 true 表示命令已成功投递给游戏。
    /// </summary>
    public bool RequestExit(bool inactive = false)
    {
        // 预检没通过就不碰游戏，避免崩溃
        if (!Available)
        {
            LastResult = "退本不可用：" + UnavailableReason;
            log.Warning("[TreasureLedger] 拒绝退本请求：{Reason}", UnavailableReason);
            return false;
        }

        try
        {
            var kind = inactive ? KindInactive : KindNormal;

            var ok = GameMain.ExecuteCommand(CommandLeaveDuty, kind, 0, 0, 0);

            if (ok)
            {
                LeaveCount++;
                LastResult = $"已发送退本命令（kind={kind}）";
                log.Information("[TreasureLedger] 退本命令已发送，kind={Kind}", kind);
            }
            else
            {
                LastResult = "退本命令被游戏拒绝（可能不在副本中）";
                log.Warning("[TreasureLedger] 退本命令返回 false");
            }

            return ok;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 退本命令异常");
            LastResult = "异常：" + ex.Message;
            return false;
        }
    }
}
