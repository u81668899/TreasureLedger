using System;

namespace TreasureLedger.Loot;

/// <summary>
/// 7.57 实测确认的 LogMessageId。
/// 掉落走 1232/1233/1254，参数 0 直接是 ItemId，不需要从文本解析。
/// </summary>
public static class LogIds
{
    /// <summary>获得了"物品"。参数：[0]=ItemId</summary>
    public const uint ItemObtained = 1232;

    /// <summary>获得了"物品"×N。参数：[0]=ItemId, [1]=数量</summary>
    public const uint ItemObtainedMultiple = 1233;

    /// <summary>获得了新的战利品（投掷分配）。参数：[0]=ItemId, [1]=数量</summary>
    public const uint LootRollReceived = 1254;

    /// <summary>获得了N金币。参数：[0]=金额</summary>
    public const uint GilObtained = 1258;

    /// <summary>获得了N个诗学/神典石。参数：[0]=数量, [1]=货币类型</summary>
    public const uint TomestoneObtained = 2165;

    /// <summary>“"XX"”任务开始。参数：[0]=副本名</summary>
    public const uint DutyStarted = 1531;

    /// <summary>将陷阱引来的魔物全部打倒了！（地图数 +1）</summary>
    public const uint TrapMobsCleared = 3765;

    /// <summary>通往宝物库的传送魔纹出现了！</summary>
    public const uint PortalAppeared = 3770;

    /// <summary>打开了通往XX的大门！（层数）参数：[0]=区域名</summary>
    public const uint GateOpenedA = 6998;
    public const uint GateOpenedB = 9365;

    /// <summary>XX的封锁解除了……（最终区）参数：[0]=区域名</summary>
    public const uint FinalAreaA = 2014;
    public const uint FinalAreaB = 2404;

    /// <summary>门发光了！</summary>
    public const uint DoorGlowingA = 6975;
    public const uint DoorGlowingB = 6976;

    /// <summary>加入了小队。</summary>
    public const uint JoinedParty = 60;
    public const uint JoinedPartyOf = 61;

    /// <summary>开始招募队员。</summary>
    public const uint RecruitStartedA = 979;
    public const uint RecruitStartedB = 980;

    /// <summary>正式加入了通讯贝"XX"。</summary>
    public const uint JoinedLinkshellA = 74;
    public const uint JoinedLinkshellB = 75;

    /// <summary>“"XX"”任务结束了。参数：[0]=副本名</summary>
    public const uint DutyEnded = 1534;
}
