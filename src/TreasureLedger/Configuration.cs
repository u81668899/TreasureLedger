using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace TreasureLedger;

/// <summary>可播报的聊天频道。</summary>
public enum ReportChannel
{
    /// <summary>默语 /e</summary>
    Echo,

    /// <summary>小队 /p</summary>
    Party,

    /// <summary>本服通讯贝 /l1</summary>
    LinkshellLocal,

    /// <summary>跨服通讯贝 /cwl1</summary>
    CrossWorldLinkshell,
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── 指令 ──────────────────────────────────────────
    /// <summary>查询指令（默认「挖宝查询」）。</summary>
    public string QueryCommand { get; set; } = "挖宝查询";

    /// <summary>重置指令（默认「挖宝重置」）。</summary>
    public string ResetCommand { get; set; } = "挖宝重置";

    // ── 频道 ──────────────────────────────────────────
    /// <summary>启用的播报频道，默认只有默语。</summary>
    public List<ReportChannel> EnabledChannels { get; set; } = new() { ReportChannel.Echo };

    /// <summary>本服贝序号 1-8。</summary>
    public int LocalLinkshellIndex { get; set; } = 1;

    /// <summary>跨服贝序号 1-8。</summary>
    public int CrossLinkshellIndex { get; set; } = 1;

    // ── 物品开关 ──────────────────────────────────────
    /// <summary>被关闭统计的物品 ID。默认空 = 全部统计。</summary>
    public List<uint> DisabledItemIds { get; set; } = new();

    // ── 行为 ──────────────────────────────────────────
    /// <summary>加入小队/招募/通讯贝时自动重置。</summary>
    public bool ResetOnPartyJoin { get; set; } = true;

    /// <summary>掉落时在本地聊天栏打印（调试用）。</summary>
    public bool PrintLootLocally { get; set; } = false;

    /// <summary>启用日志探针（排错用，平时关掉）。</summary>
    public bool EnableLogProbe { get; set; } = false;

    public HashSet<uint> DisabledSet() => new(DisabledItemIds);

    public bool IsItemEnabled(uint itemId) => !DisabledItemIds.Contains(itemId);

    public void ToggleItem(uint itemId)
    {
        if (DisabledItemIds.Contains(itemId)) DisabledItemIds.Remove(itemId);
        else DisabledItemIds.Add(itemId);
    }

    public bool IsChannelEnabled(ReportChannel c) => EnabledChannels.Contains(c);

    public void SetChannel(ReportChannel c, bool on)
    {
        if (on && !EnabledChannels.Contains(c)) EnabledChannels.Add(c);
        if (!on) EnabledChannels.Remove(c);
    }

    /// <summary>频道 → 游戏内指令前缀。</summary>
    public string ChannelSlash(ReportChannel c) => c switch
    {
        ReportChannel.Echo => "/e ",
        ReportChannel.Party => "/p ",
        ReportChannel.LinkshellLocal => $"/l{LocalLinkshellIndex} ",
        ReportChannel.CrossWorldLinkshell => $"/cwl{CrossLinkshellIndex} ",
        _ => "/e ",
    };

    public static string ChannelName(ReportChannel c) => c switch
    {
        ReportChannel.Echo => "默语",
        ReportChannel.Party => "小队",
        ReportChannel.LinkshellLocal => "本服贝",
        ReportChannel.CrossWorldLinkshell => "跨服贝",
        _ => c.ToString(),
    };

    // ── 持久化 ────────────────────────────────────────
    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);

    public static Configuration Load(IDalamudPluginInterface pi)
        => pi.GetPluginConfig() as Configuration ?? new Configuration();
}
