using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Items;
using TreasureLedger.Storage;

namespace TreasureLedger.UI;

/// <summary>长期统计表格。</summary>
public sealed class HistoryWindow
{
    private readonly HistoryStore store;
    private readonly Configuration config;

    private TimeRange currentRange = TimeRange.ThisMonth;
    private bool showAggregate = false;
    private bool byWeek = false;

    private List<RunRecord>? cachedRecords;
    private TimeRange cachedRange = (TimeRange)(-1);
    private DateTime cachedAt = DateTime.MinValue;

    // 物品明细显示开关
    private bool showItemDetail = true;

    public HistoryWindow(HistoryStore store, Configuration config)
    {
        this.store = store;
        this.config = config;
    }

    /// <summary>数据变更后让缓存失效。</summary>
    public void Invalidate()
    {
        cachedRecords = null;
        cachedRange = (TimeRange)(-1);
    }

    public void Draw()
    {
        DrawRangeBar();
        ImGui.Separator();

        var records = GetRecords();

        DrawViewToggle();

        if (records.Count == 0)
        {
            ImGui.TextDisabled("这个时间段还没有记录。");
            return;
        }

        if (showAggregate) DrawAggregateTable(records);
        else DrawRunTable(records);
    }

    private void DrawRangeBar()
    {
        ImGui.TextDisabled("时间范围");

        var ranges = new (TimeRange Range, string Label)[]
        {
            (TimeRange.Last24Hours, "24小时"),
            (TimeRange.Today, "今天"),
            (TimeRange.Last3Days, "3天"),
            (TimeRange.ThisWeek, "本周"),
            (TimeRange.ThisMonth, "本月"),
            (TimeRange.ThisQuarter, "本季度"),
            (TimeRange.Last6Months, "半年"),
            (TimeRange.ThisYear, "今年"),
            (TimeRange.All, "全部"),
        };

        for (var i = 0; i < ranges.Length; i++)
        {
            var (range, label) = ranges[i];
            var selected = currentRange == range;

            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.8f, 1f));
            if (ImGui.Button(label)) currentRange = range;
            if (selected) ImGui.PopStyleColor();

            if (i < ranges.Length - 1) ImGui.SameLine();
        }
    }

    private void DrawViewToggle()
    {
        ImGui.Spacing();

        if (ImGui.RadioButton("按行程", !showAggregate)) showAggregate = false;
        ImGui.SameLine();
        if (ImGui.RadioButton("按时间段", showAggregate)) showAggregate = true;

        if (showAggregate)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.BeginCombo("##agg", byWeek ? "按周" : "按天"))
            {
                if (ImGui.Selectable("按天", !byWeek)) byWeek = false;
                if (ImGui.Selectable("按周", byWeek)) byWeek = true;
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();
        ImGui.Checkbox("显示物品明细", ref showItemDetail);

        ImGui.Spacing();
    }

    private List<RunRecord> GetRecords()
    {
        // 5 秒内同范围不重复读盘
        if (cachedRecords is not null
            && cachedRange == currentRange
            && (DateTime.Now - cachedAt).TotalSeconds < 5)
        {
            return cachedRecords;
        }

        cachedRecords = store.Query(currentRange);
        cachedRange = currentRange;
        cachedAt = DateTime.Now;
        return cachedRecords;
    }

    // ── 按行程 ──────────────────────────────────────

    private void DrawRunTable(List<RunRecord> records)
    {
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

        var cols = showItemDetail ? 6 : 5;
        if (!ImGui.BeginTable("tl-runs", cols, flags, new Vector2(0, 400))) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("副本", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("层/底/箱", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("金币", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        if (showItemDetail) ImGui.TableSetupColumn("明细", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (var r in records.OrderByDescending(x => x.StartedAt))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(r.StartedAt.ToString("MM-dd HH:mm"));

            ImGui.TableNextColumn();
            ImGui.Text(r.DungeonName);

            ImGui.TableNextColumn();
            ImGui.Text($"{r.MaxFloor}/{r.BottomCount}/{r.ChestsOpened}");

            ImGui.TableNextColumn();
            ImGui.Text($"{r.Gil:n0}");

            ImGui.TableNextColumn();
            var kinds = r.Items.Count;
            var total = r.Items.Values.Sum();
            ImGui.Text($"{kinds}种/{total}件");

            if (showItemDetail)
            {
                ImGui.TableNextColumn();
                var parts = r.Items
                    .Select(kv => (Id: kv.Key, Count: kv.Value))
                    .OrderByDescending(x => x.Count)
                    .Select(x =>
                    {
                        var item = TrackedItems.FindBuiltin(x.Id);
                        var name = item?.Display ?? $"#{x.Id}";
                        return $"{name}×{x.Count}";
                    });
                ImGui.TextWrapped(string.Join("　", parts));
            }
        }

        ImGui.EndTable();
    }

    // ── 按时间段 ────────────────────────────────────

    private void DrawAggregateTable(List<RunRecord> records)
    {
        var rows = byWeek ? store.AggregateByWeek(records) : store.AggregateByDay(records);

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("tl-agg", 6, flags, new Vector2(0, 400))) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("次数", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("最深层", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("底数", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("金币", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(row.Key);

            ImGui.TableNextColumn();
            ImGui.Text(row.RunCount.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(row.MaxFloor.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(row.BottomCount.ToString());

            ImGui.TableNextColumn();
            ImGui.Text($"{row.Gil:n0}");

            ImGui.TableNextColumn();
            var parts = row.Items
                .OrderByDescending(kv => kv.Value)
                .Select(kv =>
                {
                    var item = TrackedItems.FindBuiltin(kv.Key);
                    var name = item?.Display ?? $"#{kv.Key}";
                    return $"{name}×{kv.Value}";
                });
            ImGui.TextWrapped(string.Join("　", parts));
        }

        ImGui.EndTable();
    }
}
