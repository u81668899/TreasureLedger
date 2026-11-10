using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Teleport;

/// <summary>
/// 传送相关的配置。
/// </summary>
public sealed class TeleportConfig
{
    /// <summary>启用传送提醒（聊天栏）。</summary>
    public bool EnableNotify { get; set; } = true;

    /// <summary>启用自动确认。</summary>
    public bool EnableAutoConfirm { get; set; } = false;

    /// <summary>
    /// 只在「检测到自己在挖宝队伍」时生效。
    /// 关闭则任何时候都生效（不推荐）。
    /// </summary>
    public bool OnlyInTreasureParty { get; set; } = true;

    /// <summary>
    /// 判定挖宝队伍的依据：进入过宝库，或最近有过挖宝活动。
    /// 窗口时长（分钟）。
    /// </summary>
    public int TreasurePartyWindowMinutes { get; set; } = 120;
}

/// <summary>
/// 挖宝队伍检测。
///
/// 判定「自己在挖宝队伍」需要满足：
///   1. 在小队里（PartyList.Count &gt; 1）
///   2. 且最近有挖宝活动（首次进宝库 / 打过宝图怪）
///
/// 只看「在队伍里」不够 —— 打别的本也在队伍里。
/// 所以必须叠加挖宝上下文。
/// </summary>
public sealed class TreasurePartyDetector
{
    private readonly IPartyList partyList;
    private readonly Maps.TreasureContext treasureContext;

    /// <summary>最近一次进入宝库的时间。</summary>
    private DateTime lastVaultEntry = DateTime.MinValue;

    public TreasurePartyDetector(IPartyList partyList, Maps.TreasureContext treasureContext)
    {
        this.partyList = partyList;
        this.treasureContext = treasureContext;
    }

    /// <summary>在小队里。</summary>
    public bool InParty
    {
        get
        {
            try { return partyList.Length > 1; }
            catch { return false; }
        }
    }

    /// <summary>标记一次进宝库。</summary>
    public void MarkVaultEntry() => lastVaultEntry = DateTime.Now;

    /// <summary>判定是否处于挖宝队伍中。</summary>
    public bool IsTreasureParty(int windowMinutes)
    {
        if (!InParty) return false;

        var window = TimeSpan.FromMinutes(windowMinutes);

        if (lastVaultEntry != DateTime.MinValue
            && DateTime.Now - lastVaultEntry <= window)
            return true;

        // 兜底：走挖宝上下文（野外打宝图怪也算）
        return treasureContext.IsTreasureContext(false, windowMinutes);
    }

    /// <summary>队伍成员数。</summary>
    public int PartySize
    {
        get
        {
            try { return partyList.Length; }
            catch { return 0; }
        }
    }
}
