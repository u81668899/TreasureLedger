using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace TreasureLedger.Maps;

/// <summary>
/// 退出副本。
///
/// 实现来源：反编译 DailyRoutines 的 LeaveDutyCommand 模块，
/// 它最终调用 OmenTools 的 DutyCommand.Leave()：
///
///     OmenServiceBase&lt;ExecuteCommandManager&gt;.Instance()
///         .ExecuteCommand(ExecuteCommandFlag.LeaveDuty, (uint)kind);
///
/// 而 ExecuteCommandManager 内部 hook 的是
/// FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteCommand。
///
/// 所以我们直接用 CS 官方的静态方法，不依赖任何第三方插件：
///
///     GameMain.ExecuteCommand(819, 0, 0, 0, 0);   // 819 = LeaveDuty
///
/// 参数：
///   command = 819  (ExecuteCommandFlag.LeaveDuty)
///   param1  = 0 = Normal（正常退出）
///             1 = Inactive（挂机/无操作退出，用于「不活跃被踢」场景）
///
/// 相比之前走过的弯路：
///   - 不用 /dutyfinder 打开界面再点按钮（依赖 UI 布局，版本更新易碎）
///   - 不用特征码扫描（CS 已经帮我们维护好了）
///   - 不碰原生指针之外的任何 UI 结构
///
/// 这是最干净、最稳定的退本方式。
/// </summary>
public sealed class DutyExiter : IDisposable
{
    /// <summary>ExecuteCommandFlag.LeaveDuty</summary>
    private const int CommandLeaveDuty = 819;

    /// <summary>正常退出。</summary>
    private const int KindNormal = 0;

    /// <summary>不活跃退出。</summary>
    private const int KindInactive = 1;

    private readonly IPluginLog log;

    public DutyExiter(IPluginLog log)
    {
        this.log = log;
    }

    public void Dispose() { }

    /// <summary>最近一次尝试的结果，给 UI 显示。</summary>
    public string LastResult { get; private set; } = string.Empty;

    /// <summary>累计退本次数。</summary>
    public int LeaveCount { get; private set; }

    /// <summary>
    /// 请求退出副本。
    /// 返回 true 表示命令已成功投递给游戏。
    /// </summary>
    /// <param name="inactive">true 用「不活跃」方式退出。</param>
    public bool RequestExit(bool inactive = false)
    {
        try
        {
            var kind = inactive ? KindInactive : KindNormal;

            // 必须在 framework 线程调用
            var ok = GameMain.ExecuteCommand(CommandLeaveDuty, kind, 0, 0, 0);

            if (ok)
            {
                LeaveCount++;
                LastResult = $"已发送退本命令（kind={kind}）";
                log.Information("[TreasureLedger] 退本命令已发送，kind={Kind}", kind);
            }
            else
            {
                LastResult = "退本命令被游戏拒绝";
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
