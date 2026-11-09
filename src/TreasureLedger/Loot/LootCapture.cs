using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Plugin.Services;
using TreasureLedger.Items;
using TreasureLedger.Loot;

namespace TreasureLedger.Loot;

/// <summary>
/// 掉落捕获：订阅 LogMessage，按 LogMessageId 分流。
/// 7.57 实测：1232=无数量掉落，1233=带数量，1254=投掷战利品，1258=金币。
/// 参数类型为 SeStringParameter，UIntValue 直接就是 ItemId / 数量。
/// </summary>
public sealed class LootCapture : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly ItemCatalog catalog;

    public event Action<LootEntry>? LootReceived;
    public event Action<long>? GilReceived;
    public event Action<uint, string>? LogSeen;

    public LootCapture(IChatGui chat, IPluginLog log, ItemCatalog catalog)
    {
        this.chat = chat;
        this.log = log;
        this.catalog = catalog;
        chat.LogMessage += OnLogMessage;
    }

    public void Dispose() => chat.LogMessage -= OnLogMessage;

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            switch (message.LogMessageId)
            {
                case LogIds.ItemObtained:
                    HandleItem(message, LootSource.Monster, useCount: false);
                    break;

                case LogIds.ItemObtainedMultiple:
                    HandleItem(message, LootSource.Chest, useCount: true);
                    break;

                case LogIds.LootRollReceived:
                    HandleItem(message, LootSource.Roll, useCount: true);
                    break;

                case LogIds.GilObtained:
                    HandleGil(message);
                    break;

                default:
                    LogSeen?.Invoke(message.LogMessageId, SafeText(message));
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 处理 LogMessage {Id} 失败", message.LogMessageId);
        }
    }

    private void HandleItem(ILogMessage message, LootSource source, bool useCount)
    {
        var itemId = GetUInt(message, 0);
        if (itemId == 0) return;

        var count = 1;
        if (useCount)
        {
            var c = GetUInt(message, 1);
            if (c > 0) count = (int)c;
        }

        var entry = new LootEntry
        {
            ItemId = itemId,
            ItemName = catalog.GetName(itemId),
            Count = count,
            Source = source,
            Time = DateTime.Now,
        };

        LootReceived?.Invoke(entry);
    }

    private void HandleGil(ILogMessage message)
    {
        var gil = GetUInt(message, 0);
        if (gil > 0) GilReceived?.Invoke(gil);
    }

    /// <summary>取第 index 个参数的无符号整数值。</summary>
    private static uint GetUInt(ILogMessage message, int index)
    {
        if (index < 0 || index >= message.ParameterCount) return 0;

        try
        {
            var p = message.Parameters[index];

            // 正常路径：强类型
            if (p is SeStringParameter sp)
                return sp.IsString ? 0u : sp.UIntValue;

            // 兜底：ToString 里是纯数字
            var s = p.ToString();
            return uint.TryParse(s, out var v) ? v : 0u;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>取第 index 个参数的文本值。</summary>
    private static string GetText(ILogMessage message, int index)
    {
        if (index < 0 || index >= message.ParameterCount) return string.Empty;
        try
        {
            var p = message.Parameters[index];
            if (p is SeStringParameter sp)
                return sp.IsString ? sp.StringValue.ExtractText() : sp.UIntValue.ToString();
            return p.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeText(ILogMessage message)
    {
        try { return message.FormatLogMessageForDebugging().ExtractText(); }
        catch { return string.Empty; }
    }
}
