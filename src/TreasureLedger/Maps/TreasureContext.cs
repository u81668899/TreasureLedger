using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Maps;

/// <summary>
/// 挖宝上下文判定。
///
/// 为什么需要它：
/// 挖宝的投掷不只在宝库副本里出现。
/// 在野外找宝图、打宝库看门怪的时候，同样会掉道具要 roll。
/// 只看「是否在宝库副本」会漏掉野外这一段。
///
/// 判定规则：
/// 1. 在宝库副本内 → 一定是挖宝场景
/// 2. 在野外，但最近一段时间内打过宝图怪 / 开过宝图箱 / 用过宝图 → 也算
/// 3. 其他情况 → 不是
///
/// 野外事件的识别来源：
/// - 系统消息里出现「宝物库」「宝图」「魔纹」等关键词
/// - 宝图相关物品的掉落（鞣革制的隐藏地图、深层传送魔纹的地图）
/// - 打倒了宝图怪（看门怪）
/// </summary>
public sealed class TreasureContext
{
    private readonly IPluginLog log;

    /// <summary>最近一次检测到挖宝活动的时间。</summary>
    public DateTime LastTreasureActivity { get; private set; } = DateTime.MinValue;

    /// <summary>最近一次挖宝活动的描述（调试用）。</summary>
    public string LastReason { get; private set; } = string.Empty;

    /// <summary>在野外识别到挖宝活动的次数。</summary>
    public int FieldActivityCount { get; private set; }

    /// <summary>判定窗口内的挖宝上下文。</summary>
    /// <param name="inDungeon">当前是否在宝库副本里。</param>
    /// <param name="windowMinutes">野外活动的判定窗口。</param>
    public bool IsTreasureContext(bool inDungeon, int windowMinutes)
    {
        if (inDungeon) return true;

        if (LastTreasureActivity == DateTime.MinValue) return false;

        return (DateTime.Now - LastTreasureActivity).TotalMinutes <= windowMinutes;
    }

    public TreasureContext(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>标记一次挖宝活动。</summary>
    public void Mark(string reason)
    {
        LastTreasureActivity = DateTime.Now;
        LastReason = reason;
        FieldActivityCount++;
        log.Debug("[TreasureLedger] 挖宝上下文更新：{Reason}", reason);
    }

    /// <summary>尝试从聊天文本里识别野外挖宝活动。</summary>
    public bool TryMarkFromLog(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 宝图相关关键词
        if (text.Contains("宝物库", StringComparison.Ordinal)
            || text.Contains("宝图", StringComparison.Ordinal)
            || text.Contains("传送魔纹", StringComparison.Ordinal)
            || text.Contains("隐藏地图", StringComparison.Ordinal))
        {
            Mark("日志关键词");
            return true;
        }

        return false;
    }

    /// <summary>重置（新会话时）。</summary>
    public void Reset()
    {
        LastTreasureActivity = DateTime.MinValue;
        LastReason = string.Empty;
        FieldActivityCount = 0;
    }
}
