using System;

namespace TreasureLedger.Marks;

/// <summary>
/// 标记与标点配置。
///
/// 这些都是【本地标记】，只在自己屏幕上显示，队友看不到。
/// 和 XML 里 LocalOnly: true 的行为一致，不影响别人。
/// </summary>
public sealed class MarkConfig
{
    /// <summary>顺序怪自动打标记（洋葱→攻击1，茄子→攻击2，蒜头→3，番茄→4，王后→5）。</summary>
    public bool EnableOrderMarks { get; set; } = true;

    /// <summary>金币怪自动打标记（按出现顺序轮转攻击1-5）。</summary>
    public bool EnableTreasureMarks { get; set; } = true;

    /// <summary>进神殿自动放地面标点（十字四点）。</summary>
    public bool EnableShrineWaymarks { get; set; } = true;

    /// <summary>离开副本时清掉自己放的标点。</summary>
    public bool ClearWaymarksOnExit { get; set; } = true;
}
