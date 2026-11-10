using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace TreasureLedger.Marks;

/// <summary>标记类型（对应游戏内的头顶标记槽）。</summary>
public enum ObjectMark
{
    Attack1 = 0,
    Attack2 = 1,
    Attack3 = 2,
    Attack4 = 3,
    Attack5 = 4,
    Attack6 = 5,
    Attack7 = 6,
    Attack8 = 7,
}

/// <summary>地面标点（A/B/C/D）。</summary>
public enum FieldMark
{
    A = 0,
    B = 1,
    C = 2,
    D = 3,
}

/// <summary>一个标点定义。</summary>
public readonly record struct FieldMarkPoint(FieldMark Mark, Vector3 Position);

/// <summary>
/// 标记服务。
///
/// 用 FFXIVClientStructs 的 MarkingController：
///   - 头顶标记：直接写 Markers[slot] = GameObjectId
///   - 地面标点：直接写 FieldMarkers[index]（Position + Active）
///
/// 两个都是【本地标记】，只在本机显示，不发给队友，
/// 和 XML 里 `LocalOnly: true` 的行为一致。
///
/// 结构（CS 实测）：
///   MarkingController
///     +16   FixedSizeArray17&lt;GameObjectId&gt; Markers   头顶标记 17 槽
///     +480  FixedSizeArray8&lt;FieldMarker&gt;   FieldMarkers 地面标点 8 个
///   FieldMarker { Vector3 Position; int X,Y,Z; bool Active; }
/// </summary>
public sealed unsafe class MarkerService
{
    private readonly IPluginLog log;

    public MarkerService(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>最近一次操作结果。</summary>
    public string LastResult { get; private set; } = string.Empty;

    /// <summary>累计打标记次数。</summary>
    public int MarkCount { get; private set; }

    /// <summary>累计放标点次数。</summary>
    public int WaymarkCount { get; private set; }

    /// <summary>给对象打头顶标记。</summary>
    public bool MarkObject(ulong gameObjectId, ObjectMark mark)
    {
        try
        {
            var mc = MarkingController.Instance();
            if (mc is null) return false;

            var slot = (int)mark;
            var markers = mc->Markers;
            if (slot < 0 || slot >= markers.Length) return false;

            markers[slot] = (GameObjectId)gameObjectId;

            MarkCount++;
            LastResult = $"标记 {mark} → {gameObjectId:X}";
            log.Debug("[TreasureLedger] {Result}", LastResult);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 打标记失败");
            return false;
        }
    }

    /// <summary>清除某个头顶标记。</summary>
    public bool ClearObjectMark(ObjectMark mark)
    {
        try
        {
            var mc = MarkingController.Instance();
            if (mc is null) return false;

            var slot = (int)mark;
            var markers = mc->Markers;
            if (slot < 0 || slot >= markers.Length) return false;

            markers[slot] = default;
            return true;
        }
        catch { return false; }
    }

    /// <summary>放置地面标点。</summary>
    public bool PlaceFieldMarker(FieldMark mark, Vector3 position)
    {
        try
        {
            var mc = MarkingController.Instance();
            if (mc is null) return false;

            var index = (uint)mark;
            if (index >= 8) return false;

            // 走游戏函数，而不是直接写结构体。
            // 直接改 FieldMarkers 内存不会通知 UI 刷新，标点不会显示出来。
            var ok = mc->PlaceFieldMarker(index, &position);

            if (ok != 0)
            {
                WaymarkCount++;
                LastResult = $"标点 {mark} → ({position.X:F0}, {position.Y:F0}, {position.Z:F0})";
                log.Debug("[TreasureLedger] {Result}", LastResult);
                return true;
            }

            LastResult = $"标点 {mark} 放置被拒绝";
            return false;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 放置标点失败");
            return false;
        }
    }

    /// <summary>批量放置标点。</summary>
    public void PlaceFieldMarkers(ReadOnlySpan<FieldMarkPoint> points)
    {
        foreach (var p in points)
            PlaceFieldMarker(p.Mark, p.Position);
    }

    /// <summary>清除所有地面标点。</summary>
    public bool ClearFieldMarkers()
    {
        try
        {
            var mc = MarkingController.Instance();
            if (mc is null) return false;

            // 同样走游戏函数，保证 UI 同步刷新
            mc->ClearFieldMarkers();
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 清除标点失败");
            return false;
        }
    }
}

/// <summary>
/// 神殿本地标点。
///
/// 坐标直接取自 Triggernometry XML 的「8.1神殿本地标点」：
///   运河宝物库神殿 / 梦羽宝殿 / 厄尔庇斯育体宝殿：
///     A(100, 0, 83) B(117, 0, 100) C(100, 0, 117) D(83, 0, 100)
///   巡梦金库：
///     A(100, 0, 90) B(110, 0, 100) C(100, 0, 110) D(90, 0, 100)
///
/// 这是围绕场地中心的十字型四点，用于站位参考。
/// </summary>
public static class ShrineWaymarks
{
    /// <summary>老神殿的标点。</summary>
    public static readonly FieldMarkPoint[] Classic =
    {
        new(FieldMark.A, new Vector3(100f, 0f, 83f)),
        new(FieldMark.B, new Vector3(117f, 0f, 100f)),
        new(FieldMark.C, new Vector3(100f, 0f, 117f)),
        new(FieldMark.D, new Vector3(83f, 0f, 100f)),
    };

    /// <summary>巡梦金库的标点（场地更小）。</summary>
    public static readonly FieldMarkPoint[] DreamVault =
    {
        new(FieldMark.A, new Vector3(100f, 0f, 90f)),
        new(FieldMark.B, new Vector3(110f, 0f, 100f)),
        new(FieldMark.C, new Vector3(100f, 0f, 110f)),
        new(FieldMark.D, new Vector3(90f, 0f, 100f)),
    };

    /// <summary>按副本名选标点。</summary>
    public static FieldMarkPoint[]? ForDungeon(string dungeonName) => dungeonName switch
    {
        "巡梦金库" => DreamVault,
        "运河宝物库神殿" => Classic,
        "梦羽宝殿" => Classic,
        "厄尔庇斯育体宝殿" => Classic,
        _ => null,
    };
}
