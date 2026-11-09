using System;
using System.Collections.Generic;

namespace TreasureLedger.Loot;

/// <summary>
/// 自动退出的配置。
/// 默认关闭，这是会动游戏的操作。
/// </summary>
public sealed class AutoExitConfig
{
    /// <summary>总开关。默认关。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 退出前等待多久（秒）。
    /// 确认箱子开完、投掷结束之后再退出，留缓冲。
    /// </summary>
    public int WaitSeconds { get; set; } = 8;

    /// <summary>
    /// 要求投掷必须全部结束才退出。
    /// 关掉的话只看宝箱是否开完。
    /// </summary>
    public bool RequireRollsFinished { get; set; } = true;

    /// <summary>只在有下底记录时才退出（避免普通进洞误触发）。</summary>
    public bool RequireBottom { get; set; } = true;

    /// <summary>退出前在聊天栏提示。</summary>
    public bool NotifyBeforeExit { get; set; } = true;
}
