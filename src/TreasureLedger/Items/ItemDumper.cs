using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Items;

/// <summary>
/// 一次性把 Item 表里所有物品名 → ID 导出，用来建立参考清单。
/// </summary>
public static class ItemDumper
{
    public static string Dump(IDataManager data, string outPath)
    {
        var sb = new StringBuilder(2_000_000);
        var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (sheet is null) return "sheet null";

        var count = 0;
        foreach (var row in sheet)
        {
            var id = row.RowId;
            string name;
            try { name = row.Name.ExtractText() ?? string.Empty; }
            catch { continue; }
            if (name.Length == 0) continue;
            sb.Append(id).Append('\t').Append(name).Append('\n');
            count++;
        }

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        return $"exported {count} items -> {outPath}";
    }
}
