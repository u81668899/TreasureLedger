using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Items;

/// <summary>
/// 物品目录：双向查询。
/// - ID → 名字：直接从 Item 表读
/// - 名字 → ID：懒加载建立索引，首次查询时构建一次
///
/// 为什么需要双向：
/// 玩家添加物品时可能只知道名字（游戏里看不到 ID），
/// 也可能从外部拿到 ID。两种输入都要能匹配到同一个物品。
/// </summary>
public sealed class ItemCatalog
{
    private readonly IDataManager data;
    private readonly Dictionary<uint, string> nameCache = new();

    /// <summary>名字 → ID。首次按名字查询时构建。</summary>
    private Dictionary<string, uint>? nameIndex;

    public ItemCatalog(IDataManager data)
    {
        this.data = data;
    }

    /// <summary>按 ID 取游戏原名。找不到返回空串。</summary>
    public string GetName(uint itemId)
    {
        if (itemId == 0) return string.Empty;
        if (nameCache.TryGetValue(itemId, out var cached)) return cached;

        string name;
        try
        {
            var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet is not null && sheet.TryGetRow(itemId, out var row))
                name = row.Name.ExtractText() ?? string.Empty;
            else
                name = string.Empty;
        }
        catch
        {
            name = string.Empty;
        }

        nameCache[itemId] = name;
        return name;
    }

    public bool TryGetName(uint itemId, out string name)
    {
        name = GetName(itemId);
        return name.Length > 0;
    }

    /// <summary>按名字精确查 ID。命中返回 (ID, 名字)。</summary>
    public (uint ItemId, string Name)? FindByName(string name)
    {
        var key = (name ?? string.Empty).Trim();
        if (key.Length == 0) return null;

        EnsureIndex();

        if (nameIndex!.TryGetValue(key, out var id))
            return (id, key);

        return null;
    }

    /// <summary>按名字模糊查，返回前 N 个候选项。</summary>
    public List<(uint ItemId, string Name)> Search(string keyword, int limit = 20)
    {
        var result = new List<(uint, string)>();
        var key = (keyword ?? string.Empty).Trim();
        if (key.Length == 0) return result;

        EnsureIndex();

        foreach (var kv in nameIndex!)
        {
            if (kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                result.Add((kv.Value, kv.Key));
                if (result.Count >= limit) break;
            }
        }

        return result;
    }

    /// <summary>构建名字索引（一次性，约 5 万条）。</summary>
    private void EnsureIndex()
    {
        if (nameIndex is not null) return;

        var dict = new Dictionary<string, uint>(StringComparer.Ordinal);

        try
        {
            var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet is not null)
            {
                foreach (var row in sheet)
                {
                    string name;
                    try { name = row.Name.ExtractText() ?? string.Empty; }
                    catch { continue; }

                    if (name.Length == 0) continue;

                    // 同名物品保留 ID 最小的那个
                    var id = row.RowId;
                    if (!dict.TryGetValue(name, out var existing) || id < existing)
                        dict[name] = id;
                }
            }
        }
        catch
        {
            // 建索引失败不影响 ID→名字 的查询
        }

        nameIndex = dict;
    }

    public void Clear()
    {
        nameCache.Clear();
        nameIndex = null;
    }
}
