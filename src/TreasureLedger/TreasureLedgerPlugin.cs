using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TreasureLedger.Chat;
using TreasureLedger.Commands;
using TreasureLedger.Diagnostics;
using TreasureLedger.Items;
using TreasureLedger.Ledger;
using TreasureLedger.Loot;
using TreasureLedger.Maps;
using TreasureLedger.UI;

namespace TreasureLedger;

public sealed class TreasureLedgerPlugin : IDalamudPlugin
{
    public string Name => "Treasure Ledger";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IDataManager data;
    private readonly ICommandManager commands;
    private readonly IClientState clientState;

    private readonly Configuration config;
    private readonly SessionLedger ledger = new();
    private readonly ItemCatalog catalog;
    private readonly LootDeduplicator dedup = new();
    private readonly LootCapture capture;
    private readonly ChatSender sender;
    private readonly DungeonTracker tracker;
    private readonly ChatCommandListener chatCommands;
    private readonly LogProbe probe;
    private readonly WindowSystem windowSystem = new("TreasureLedger");
    private readonly ConfigWindow configWindow;
    private DateTime lastPrune = DateTime.Now;

    public TreasureLedgerPlugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui chat,
        IPluginLog log,
        IDataManager data,
        ICommandManager commands,
        IClientState clientState)
    {
        this.pluginInterface = pluginInterface;
        this.chat = chat;
        this.log = log;
        this.data = data;
        this.commands = commands;
        this.clientState = clientState;

        config = Configuration.Load(pluginInterface);
        catalog = new ItemCatalog(data);
        sender = new ChatSender(chat, log) { PreviewOnly = config.ChannelPreviewOnly };

        tracker = new DungeonTracker(clientState, log, ledger, config, ResetLedger);

        capture = new LootCapture(chat, log, catalog);
        capture.LootReceived += OnLoot;
        capture.RollNotified += OnRollNotified;
        capture.GilReceived += OnGil;
        capture.LogSeen += OnLogSeen;

        chatCommands = new ChatCommandListener(
            chat, log, config, ledger, sender, ResetLedger);

        configWindow = new ConfigWindow(
            config, ledger, tracker, catalog,
            SaveConfig, ResetLedger,
            s => sender.PrintLocal(s),
            b => sender.PreviewOnly = b);

        windowSystem.AddWindow(configWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        probe = new LogProbe(log);
        if (config.EnableLogProbe) probe.Start(chat);

        chat.Print($"[TreasureLedger] 已加载　输入「{config.QueryCommand}」查询，「{config.ResetCommand}」重置");
        log.Information("[TreasureLedger] 加载完成");
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        windowSystem.RemoveAllWindows();

        capture.LootReceived -= OnLoot;
        capture.RollNotified -= OnRollNotified;
        capture.GilReceived -= OnGil;
        capture.LogSeen -= OnLogSeen;
        capture.Dispose();

        chatCommands.Dispose();
        tracker.Dispose();
        sender.Dispose();
        probe.Stop(chat);
        probe.Dispose();

        log.Information("[TreasureLedger] 已卸载");
    }

    private void ToggleConfigUi() => configWindow.Toggle();

    private void SaveConfig() => config.Save(pluginInterface);

    private void ResetLedger()
    {
        ledger.Reset();
        dedup.Reset();
    }

    /// <summary>投掷通知：登记件数，等进包时兑现。</summary>
    private void OnRollNotified(uint itemId, int count)
    {
        if (!config.IsItemEnabled(itemId)) return;
        dedup.RegisterRoll(itemId, count);
        log.Debug("[TreasureLedger] 投掷登记 {Item}×{N}，待兑现 {P}",
            catalog.GetName(itemId), count, dedup.PendingFor(itemId));
    }

    /// <summary>进包掉落：按投掷队列兑现，剩余按原来源计。</summary>
    private void OnLoot(LootEntry entry)
    {
        if (!config.IsItemEnabled(entry.ItemId))
        {
            log.Debug("[TreasureLedger] 已关闭物品，跳过：{Item}", entry.ItemName);
            return;
        }

        var r = dedup.Resolve(entry.ItemId, entry.Count, entry.Source);

        if (r.Count > 0)
            ledger.AddItem(entry.ItemId, r.Count, r.Source);

        if (r.HasLeftover)
            ledger.AddItem(entry.ItemId, r.Leftover, r.LeftoverSource);

        if (config.PrintLootLocally)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.Count > 0) parts.Add($"+{r.Count} {r.Source}");
            if (r.HasLeftover) parts.Add($"+{r.Leftover} {r.LeftoverSource}");
            chat.Print($"[TreasureLedger] {entry.ItemName}  {string.Join("  ", parts)}");
        }

        if (DateTime.Now - lastPrune > TimeSpan.FromMinutes(5))
        {
            dedup.Prune();
            lastPrune = DateTime.Now;
        }
    }

    private void OnGil(long amount) => ledger.Gil += amount;

    private void OnLogSeen(uint id, string text) => tracker.OnLog(id, text);
}
