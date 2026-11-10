using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Storage;

/// <summary>查询的时间范围。</summary>
public enum TimeRange
{
    Last24Hours,
    Today,
    Last3Days,
    ThisWeek,
    ThisMonth,
    ThisQuarter,
    Last6Months,
    ThisYear,
    All,
}

/// <summary>
/// 长期存储。
///
/// 设计要点（按需求）：
/// - 按「月」分卷，一个文件一个月，避免单文件过大
/// - 查询时只打开命中的分卷，不加载全部历史
/// - 每次查询结果缓存，同范围重复查询不重复读盘
///
/// 目录结构：
///   pluginConfigs/TreasureLedger/history/
///     2026-11.json
///     2026-12.json
///     index.json   （可选，记录各卷的起止时间与条目数，加速范围判断）
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly IPluginLog log;
    private readonly string historyDir;

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>已加载的分卷缓存：文件名 → 记录列表。</summary>
    private readonly Dictionary<string, List<RunRecord>> loaded = new();

    /// <summary>当前正在写入的分卷（当月）。</summary>
    private string currentPartitionKey = string.Empty;
    private List<RunRecord>? currentPartition;

    public HistoryStore(IPluginLog log)
    {
        this.log = log;

        historyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN", "pluginConfigs", "TreasureLedger", "history");

        Directory.CreateDirectory(historyDir);
    }

    public string HistoryDirectory => historyDir;

    public void Dispose()
    {
        FlushCurrent();
        loaded.Clear();
    }

    // ── 分卷 Key ────────────────────────────────────

    private static string PartitionKey(DateTime t) => t.ToString("yyyy-MM");

    private string PartitionPath(string key) => Path.Combine(historyDir, key + ".json");

    // ── 写入 ────────────────────────────────────────

    /// <summary>追加一条行程记录。</summary>
    public void Append(RunRecord record)
    {
        try
        {
            var key = PartitionKey(record.StartedAt);
            EnsureCurrentPartition(key);

            currentPartition!.Add(record);

            // 缓存同步
            if (!loaded.TryGetValue(key, out var cached))
            {
                loaded[key] = currentPartition!;
            }

            SavePartition(key);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 写入历史记录失败");
        }
    }

    private void EnsureCurrentPartition(string key)
    {
        if (currentPartition is not null && currentPartitionKey == key) return;

        FlushCurrent();

        currentPartitionKey = key;
        currentPartition = LoadPartition(key) ?? new List<RunRecord>();
        loaded[key] = currentPartition;
    }

    private void FlushCurrent()
    {
        if (currentPartition is null) return;
        try { SavePartition(currentPartitionKey); } catch { }
        currentPartition = null;
        currentPartitionKey = string.Empty;
    }

    private void SavePartition(string key)
    {
        if (!loaded.TryGetValue(key, out var list)) return;

        try
        {
            var json = JsonSerializer.Serialize(list, jsonOptions);
            var path = PartitionPath(key);

            // 先写临时文件再替换，避免写一半损坏
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 保存分卷 {Key} 失败", key);
        }
    }

    // ── 读取 ────────────────────────────────────────

    private List<RunRecord>? LoadPartition(string key)
    {
        if (loaded.TryGetValue(key, out var cached)) return cached;

        var path = PartitionPath(key);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<RunRecord>>(json, jsonOptions);
            if (list is not null) loaded[key] = list;
            return list;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 读取分卷 {Key} 失败", key);
            return null;
        }
    }

    /// <summary>所有存在的分卷 key，倒序（最新的在前）。</summary>
    public List<string> ListPartitions()
    {
        try
        {
            return Directory.EnumerateFiles(historyDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(k => k is not null && k.Length == 7 && k[4] == '-')
                .Select(k => k!)
                .OrderByDescending(k => k, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 计算时间范围的起点。
    /// </summary>
    public static DateTime? RangeStart(TimeRange range, DateTime now)
    {
        return range switch
        {
            TimeRange.Last24Hours => now.AddDays(-1),
            TimeRange.Today => now.Date,
            TimeRange.Last3Days => now.Date.AddDays(-3),
            TimeRange.ThisWeek => now.Date.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1)),
            TimeRange.ThisMonth => new DateTime(now.Year, now.Month, 1),
            TimeRange.ThisQuarter => new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1),
            TimeRange.Last6Months => now.AddMonths(-6),
            TimeRange.ThisYear => new DateTime(now.Year, 1, 1),
            TimeRange.All => null,
            _ => null,
        };
    }

    /// <summary>
    /// 按时间范围查询。只打开命中的分卷。
    /// </summary>
    public List<RunRecord> Query(TimeRange range)
    {
        var now = DateTime.Now;
        var start = RangeStart(range, now);

        var result = new List<RunRecord>();
        var partitions = ListPartitions();

        foreach (var key in partitions)
        {
            // 分卷是按月的。若该月的最后一天都早于范围起点，整卷跳过不读。
            if (start is not null && IsPartitionBefore(key, start.Value)) continue;

            var list = LoadPartition(key);
            if (list is null) continue;

            foreach (var r in list)
            {
                if (start is not null && r.StartedAt < start.Value) continue;
                result.Add(r);
            }
        }

        log.Debug("[TreasureLedger] 查询 {Range}：命中 {N} 条，读取 {P} 个分卷",
            range, result.Count, partitions.Count);

        return result;
    }

    /// <summary>该月分卷是否整体早于给定时间（早于则无需读取）。</summary>
    private static bool IsPartitionBefore(string key, DateTime time)
    {
        if (key.Length != 7) return false;
        if (!int.TryParse(key.AsSpan(0, 4), out var year)) return false;
        if (!int.TryParse(key.AsSpan(5, 2), out var month)) return false;

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);
        return monthEnd <= time;
    }

    // ── 聚合 ────────────────────────────────────────

    /// <summary>按天聚合。</summary>
    public List<AggregateRow> AggregateByDay(IEnumerable<RunRecord> records)
        => Aggregate(records, r => r.StartedAt.Date, "yyyy-MM-dd");

    /// <summary>按周聚合。</summary>
    public List<AggregateRow> AggregateByWeek(IEnumerable<RunRecord> records)
        => Aggregate(records, WeekStart, "yyyy-MM-dd");

    /// <summary>按月聚合。</summary>
    public List<AggregateRow> AggregateByMonth(IEnumerable<RunRecord> records)
        => Aggregate(records, r => new DateTime(r.StartedAt.Year, r.StartedAt.Month, 1), "yyyy-MM");

    private static DateTime WeekStart(RunRecord r)
    {
        var d = r.StartedAt.Date;
        var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
        return d.AddDays(-diff);
    }

    private List<AggregateRow> Aggregate(
        IEnumerable<RunRecord> records,
        Func<RunRecord, DateTime> keySelector,
        string format)
    {
        var groups = records.GroupBy(keySelector);

        var rows = new List<AggregateRow>();
        foreach (var g in groups.OrderByDescending(g => g.Key))
        {
            var row = new AggregateRow
            {
                Key = g.Key.ToString(format),
                RunCount = g.Count(),
                MaxFloor = g.Max(r => r.MaxFloor),
                BottomCount = g.Sum(r => r.BottomCount),
                Chests = g.Sum(r => r.ChestsOpened),
                Gil = g.Sum(r => r.Gil),
                Items = new Dictionary<uint, int>(),
            };

            foreach (var r in g)
            {
                foreach (var kv in r.Items)
                    row.Items[kv.Key] = row.Items.GetValueOrDefault(kv.Key) + kv.Value;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>清空所有历史（危险操作，仅 UI 明确确认后调用）。</summary>
    public void ClearAll()
    {
        loaded.Clear();
        currentPartition = null;
        currentPartitionKey = string.Empty;

        foreach (var f in Directory.EnumerateFiles(historyDir, "*.json").ToList())
        {
            try { File.Delete(f); } catch { }
        }
    }
}

/// <summary>聚合后的一行。</summary>
public sealed class AggregateRow
{
    public string Key { get; set; } = string.Empty;
    public int RunCount { get; set; }
    public int MaxFloor { get; set; }
    public int BottomCount { get; set; }
    public int Chests { get; set; }
    public long Gil { get; set; }
    public Dictionary<uint, int> Items { get; set; } = new();
}
