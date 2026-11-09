using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace TreasureLedger;

/// <summary>可响应/播报的聊天频道。</summary>
public enum ReportChannel
{
    /// <summary>默语 /e</summary>
    Echo,

    /// <summary>小队 /p</summary>
    Party,

    /// <summary>本服通讯贝 /l1-8</summary>
    LinkshellLocal,

    /// <summary>跨服通讯贝 /cwl1-8</summary>
    CrossWorldLinkshell,
}

/// <summary>宝图分类，顺序与 Triggernometry XML 一致。</summary>
public enum MapTier
{
    G1G8,
    G10,
    G12,
    G14G15,
    DeepGreen,
    G17G18,
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // ── 指令 ──────────────────────────────────────────
    public string QueryCommand { get; set; } = "挖宝查询";
    public string ResetCommand { get; set; } = "挖宝重置";

    // ── 频道 ──────────────────────────────────────────
    /// <summary>允许响应查询的频道，默认只有默语。</summary>
    public List<ReportChannel> ListenChannels { get; set; } = new() { ReportChannel.Echo };

    public int LocalLinkshellIndex { get; set; } = 1;
    public int CrossLinkshellIndex { get; set; } = 1;

    // ── 物品 ──────────────────────────────────────────
    /// <summary>被关闭统计的内置物品 ID。</summary>
    public List<uint> DisabledItemIds { get; set; } = new();

    /// <summary>用户自行添加的物品（版本更新后自己补，不用等插件更新）。</summary>
    public List<CustomItem> CustomItems { get; set; } = new();

    // ── 行为 ──────────────────────────────────────────
    public bool ResetOnPartyJoin { get; set; } = true;
    public bool PrintLootLocally { get; set; } = false;
    public bool EnableLogProbe { get; set; } = false;

    public bool IsItemEnabled(uint itemId) => !DisabledItemIds.Contains(itemId);

    public void ToggleItem(uint itemId)
    {
        if (DisabledItemIds.Contains(itemId)) DisabledItemIds.Remove(itemId);
        else DisabledItemIds.Add(itemId);
    }

    public bool IsListenChannel(ReportChannel c) => ListenChannels.Contains(c);

    public void SetListenChannel(ReportChannel c, bool on)
    {
        if (on && !ListenChannels.Contains(c)) ListenChannels.Add(c);
        if (!on) ListenChannels.Remove(c);
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

    public static string TierName(MapTier t) => t switch
    {
        MapTier.G1G8 => "G1-G8",
        MapTier.G10 => "G10",
        MapTier.G12 => "G12",
        MapTier.G14G15 => "G14-G15",
        MapTier.DeepGreen => "深层绿",
        MapTier.G17G18 => "G17-G18",
        _ => t.ToString(),
    };

    // ── 持久化 ────────────────────────────────────────
    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);

    public static Configuration Load(IDalamudPluginInterface pi)
        => pi.GetPluginConfig() as Configuration ?? new Configuration();
}

/// <summary>用户自己添加的物品。</summary>
public sealed class CustomItem
{
    public uint ItemId { get; set; }
    public string Display { get; set; } = string.Empty;
    public MapTier Tier { get; set; } = MapTier.G17G18;

    /// <summary>用户添加的条目可以被删除；内置条目不能。</summary>
    public bool UserAdded { get; set; } = true;
}
