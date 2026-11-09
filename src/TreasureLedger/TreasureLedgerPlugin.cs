using System;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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
    private readonly DungeonTracker tracker;
    private readonly CommandRouter router;
    private readonly LogProbe probe;
    private readonly WindowSystem windowSystem = new("TreasureLedger");
    private readonly ConfigWindow configWindow;

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

        // 状态机
        tracker = new DungeonTracker(clientState, log, ledger, config, ResetLedger);
        tracker.StateChanged += () => { };

        // 掉落捕获
        capture = new LootCapture(chat, log, catalog);
        capture.LootReceived += OnLoot;
        capture.GilReceived += OnGil;
        capture.LogSeen += OnLogSeen;

        // 指令
        router = new CommandRouter(commands, chat, log, config, ledger, SaveConfig, ResetLedger);

        // UI
        configWindow = new ConfigWindow(
            config, ledger, tracker,
            SaveConfig, ResetLedger,
            () => router.Rebind(),
            s => commands.ProcessCommand(s));
        windowSystem.AddWindow(configWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        // 探针默认关
        probe = new LogProbe(log);
        if (config.EnableLogProbe) probe.Start(chat);

        chat.Print($"[TreasureLedger] 已加载　查询：/{config.QueryCommand}　重置：/{config.ResetCommand}");
        log.Information("[TreasureLedger] 加载完成");
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        windowSystem.RemoveAllWindows();

        capture.LootReceived -= OnLoot;
        capture.GilReceived -= OnGil;
        capture.LogSeen -= OnLogSeen;
        capture.Dispose();

        tracker.Dispose();
        router.Dispose();
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

    private void OnLoot(LootEntry entry)
    {
        if (!config.IsItemEnabled(entry.ItemId))
        {
            log.Debug("[TreasureLedger] 跳过已关闭物品：{Item}", entry.ItemName);
            return;
        }

        if (!dedup.ShouldAccept(entry, out var reason))
        {
            log.Debug("[TreasureLedger] 去重：{Item}（{Reason}）", entry.ItemName, reason);
            return;
        }

        ledger.AddItem(entry.ItemId, entry.Count, entry.Source);

        if (config.PrintLootLocally)
            chat.Print($"[TreasureLedger] +{entry.Count} {entry.ItemName} ({entry.Source})");

        log.Debug("[TreasureLedger] 记录掉落 {Item}×{Count} ({Source})", entry.ItemName, entry.Count, entry.Source);
    }

    private void OnGil(long amount) => ledger.Gil += amount;

    private void OnLogSeen(uint id, string text) => tracker.OnLog(id, text);
}
