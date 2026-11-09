using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Items;

/// <summary>
/// 物品目录：运行期通过 Item 表按 ID 取名字，不预置 ID 表。
/// 这样版本更新、物品改名都不用改代码。
/// </summary>
public sealed class ItemCatalog
{
    private readonly IDataManager data;
    private readonly Dictionary<uint, string> cache = new();

    public ItemCatalog(IDataManager data)
    {
        this.data = data;
    }

    public string GetName(uint itemId)
    {
        if (itemId == 0) return string.Empty;
        if (cache.TryGetValue(itemId, out var cached)) return cached;

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

        cache[itemId] = name;
        return name;
    }

    public bool TryGetName(uint itemId, out string name)
    {
        name = GetName(itemId);
        return name.Length > 0;
    }

    public void Clear() => cache.Clear();
}
