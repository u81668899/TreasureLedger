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

    /// <summary>悄悄话 /tell</summary>
    Tell,
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

    /// <summary>查询指令是否按正则匹配。</summary>
    public bool QueryCommandIsRegex { get; set; } = false;

    /// <summary>重置指令是否按正则匹配。</summary>
    public bool ResetCommandIsRegex { get; set; } = false;

    // ── 频道 ──────────────────────────────────────────
    /// <summary>允许响应查询的频道，默认只有默语。</summary>
    public List<ReportChannel> ListenChannels { get; set; } = new() { ReportChannel.Echo };

    public int LocalLinkshellIndex { get; set; } = 1;
    public int CrossLinkshellIndex { get; set; } = 1;

    /// <summary>
    /// 启用的本服贝序号（1-8，可多选）。
    /// 玩家可能同时挂在好几个贝里，查询要能回多个。
    /// </summary>
    public List<int> LocalLinkshellIndices { get; set; } = new() { 1 };

    /// <summary>启用的跨服贝序号（1-8，可多选）。</summary>
    public List<int> CrossLinkshellIndices { get; set; } = new() { 1 };

    /// <summary>悄悄话固定回复对象（可多个）。留空则回给发消息的人。</summary>
    public List<string> TellTargets { get; set; } = new();

    // ── 物品 ──────────────────────────────────────────
    /// <summary>被关闭统计的内置物品 ID。</summary>
    public List<uint> DisabledItemIds { get; set; } = new();

    /// <summary>用户自行添加的物品（版本更新后自己补，不用等插件更新）。</summary>
    public List<CustomItem> CustomItems { get; set; } = new();

    /// <summary>
    /// 内置物品的显示名覆盖：ItemId → 自定义名。
    /// 只影响播报显示，不改内置清单本身。
    /// </summary>
    public Dictionary<uint, string> BuiltinDisplayOverrides { get; set; } = new();

    // ── 行为 ──────────────────────────────────────────
    public bool ResetOnPartyJoin { get; set; } = true;
    public bool PrintLootLocally { get; set; } = false;
    public bool EnableLogProbe { get; set; } = false;

    /// <summary>
    /// 仅本地预览频道输出。
    /// 关闭后走 IChatGui.Print(XivChatEntry)，由 Type 指定频道（官方安全封装）。
    /// </summary>
    public bool ChannelPreviewOnly { get; set; } = false;

    /// <summary>自动投掷配置。</summary>
    public Loot.AutoRollConfig AutoRoll { get; set; } = new();

    /// <summary>自动退出配置。</summary>
    public Loot.AutoExitConfig AutoExit { get; set; } = new();

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

    /// <summary>频道 → 游戏内指令前缀（单值版，用于显示）。</summary>
    public string ChannelSlash(ReportChannel c) => c switch
    {
        ReportChannel.Echo => "/e ",
        ReportChannel.Party => "/p ",
        ReportChannel.LinkshellLocal => $"/l{LocalLinkshellIndices.FirstOrDefault(1)} ",
        ReportChannel.CrossWorldLinkshell => $"/cwl{CrossLinkshellIndices.FirstOrDefault(1)} ",
        ReportChannel.Tell => "/tell ",
        _ => "/e ",
    };

    /// <summary>该频道对应的所有发送目标（多个贝时会有多条）。</summary>
    public string[] ChannelSlashes(ReportChannel c) => c switch
    {
        ReportChannel.Echo => new[] { "/e " },
        ReportChannel.Party => new[] { "/p " },
        ReportChannel.LinkshellLocal => LocalLinkshellIndices
            .Where(i => i is >= 1 and <= 8)
            .DefaultIfEmpty(1)
            .Select(i => $"/l{i} ")
            .ToArray(),
        ReportChannel.CrossWorldLinkshell => CrossLinkshellIndices
            .Where(i => i is >= 1 and <= 8)
            .DefaultIfEmpty(1)
            .Select(i => $"/cwl{i} ")
            .ToArray(),
        ReportChannel.Tell => new[] { "/tell " },
        _ => new[] { "/e " },
    };

    public static string ChannelName(ReportChannel c) => c switch
    {
        ReportChannel.Echo => "默语",
        ReportChannel.Party => "小队",
        ReportChannel.LinkshellLocal => "本服贝",
        ReportChannel.CrossWorldLinkshell => "跨服贝",
        ReportChannel.Tell => "悄悄话",
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
