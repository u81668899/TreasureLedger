using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Plugin.Services;
using TreasureLedger.Items;

namespace TreasureLedger.Loot;

/// <summary>
/// 掉落捕获：订阅 LogMessage，按 LogMessageId 分流。
/// 7.57 实测：
///   1232 = 获得了"物品"          参数 [0]=ItemId
///   1233 = 获得了"物品"×N        参数 [0]=ItemId, [1]=数量
///   1254 = 获得了新的战利品      参数 [0]=ItemId, [1]=数量（投掷）
///   1258 = 获得了 N 金币         参数 [0]=金额
/// 参数类型 SeStringParameter，UIntValue 直接就是 ItemId / 数量。
/// </summary>
public sealed class LootCapture : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly ItemCatalog catalog;

    /// <summary>普通进包掉落（1232/1233）。</summary>
    public event Action<LootEntry>? LootReceived;

    /// <summary>投掷成功通知（1254），只登记不计数。</summary>
    public event Action<uint, int>? RollNotified;

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
                    HandleRoll(message);
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

        LootReceived?.Invoke(new LootEntry
        {
            ItemId = itemId,
            ItemName = catalog.GetName(itemId),
            Count = count,
            Source = source,
            Time = DateTime.Now,
        });
    }

    private void HandleRoll(ILogMessage message)
    {
        var itemId = GetUInt(message, 0);
        if (itemId == 0) return;

        var count = 1;
        var c = GetUInt(message, 1);
        if (c > 0) count = (int)c;

        RollNotified?.Invoke(itemId, count);
    }

    private void HandleGil(ILogMessage message)
    {
        var gil = GetUInt(message, 0);
        if (gil > 0) GilReceived?.Invoke(gil);
    }

    private static uint GetUInt(ILogMessage message, int index)
    {
        if (index < 0 || index >= message.ParameterCount) return 0;
        try
        {
            var p = message.Parameters[index];
            if (p is SeStringParameter sp)
                return sp.IsString ? 0u : sp.UIntValue;

            return uint.TryParse(p.ToString(), out var v) ? v : 0u;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeText(ILogMessage message)
    {
        try { return message.FormatLogMessageForDebugging().ExtractText(); }
        catch { return string.Empty; }
    }
}
