using System;
using System.Linq;
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
using TreasureLedger.Recruit;
using TreasureLedger.Storage;
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
    private readonly ISigScanner sigScanner;
    private readonly IPartyFinderGui partyFinder;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IAddonLifecycle addonLifecycle;

    private readonly Configuration config;
    private readonly SessionLedger ledger = new();
    private readonly ItemCatalog catalog;
    private readonly LootDeduplicator dedup = new();
    private readonly LootCapture capture;
    private readonly ChatSender sender;
    private readonly DungeonTracker tracker;
    private readonly ChatCommandListener chatCommands;
    private readonly LogProbe probe;
    private readonly AutoRoller roller;
    private readonly HistoryStore history;
    private readonly RecruitTracker recruit;
    private readonly RunGains runGains = new();
    private readonly TreasureContext treasureContext;
    private readonly VaultProbe vaultProbe;
    private readonly AutoExiter autoExiter;
    private readonly DutyExiter dutyExiter;
    private readonly GameCommandExecutor commandExecutor;
    private readonly AddonDumper addonDumper;

    private readonly WindowSystem windowSystem = new("TreasureLedger");
    private readonly ConfigWindow configWindow;
    private readonly HistoryWindow historyWindow;
    private readonly AutoRollWindow autoRollWindow;
    private readonly MainWindow mainWindow;

    private DateTime lastPrune = DateTime.Now;

    public TreasureLedgerPlugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui chat,
        IPluginLog log,
        IDataManager data,
        ICommandManager commands,
        IClientState clientState,
        ISigScanner sigScanner,
        IPartyFinderGui partyFinder,
        IFramework framework,
        IObjectTable objectTable,
        IAddonLifecycle addonLifecycle)
    {
        this.pluginInterface = pluginInterface;
        this.chat = chat;
        this.log = log;
        this.data = data;
        this.commands = commands;
        this.clientState = clientState;
        this.sigScanner = sigScanner;
        this.partyFinder = partyFinder;
        this.framework = framework;
        this.objectTable = objectTable;
        this.addonLifecycle = addonLifecycle;

        config = Configuration.Load(pluginInterface);
        catalog = new ItemCatalog(data);
        sender = new ChatSender(chat, log) { PreviewOnly = config.ChannelPreviewOnly };
        history = new HistoryStore(log);
        recruit = new RecruitTracker(log, partyFinder);

        treasureContext = new TreasureContext(log);
        vaultProbe = new VaultProbe(log, objectTable);
        autoExiter = new AutoExiter(log, vaultProbe, msg => sender.SendTo(ReportChannel.Echo, config, msg));
        commandExecutor = new GameCommandExecutor(log, sigScanner);
        addonDumper = new AddonDumper(log);
        dutyExiter = new DutyExiter(log);
        tracker = new DungeonTracker(clientState, log, ledger, config, ResetLedger);
        tracker.RunEnded += OnRunEnded;

        capture = new LootCapture(chat, log, catalog);
        capture.LootReceived += OnLoot;
        capture.RollNotified += OnRollNotified;
        capture.GilReceived += OnGil;
        capture.LogSeen += OnLogSeen;

        chatCommands = new ChatCommandListener(
            chat, log, config, ledger, sender, ResetLedger, () => recruit.Current, catalog);

        roller = new AutoRoller(log, sigScanner);
        var rollReady = roller.TryInitialize();

        historyWindow = new HistoryWindow(history, config);
        mainWindow = new MainWindow();
        configWindow = new ConfigWindow(
            config, ledger, tracker, catalog,
            SaveConfig, ResetLedger, s => sender.PrintLocal(s),
            b => sender.PreviewOnly = b,
            () => historyWindow.Invalidate(),
            () => recruit.Current,
            t => recruit.Set(t));

        autoRollWindow = new AutoRollWindow(
            config, roller, catalog, SaveConfig, () => historyWindow.Invalidate());

        mainWindow.Add("配置", configWindow);
        mainWindow.Add("自动投掷", autoRollWindow);
        mainWindow.Add("调试", new DebugWindow(addonDumper, commandExecutor, dutyExiter));
        mainWindow.Add("长期统计", historyWindow);

        windowSystem.AddWindow(mainWindow);
        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleMain;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMain;

        framework.Update += OnFrameworkUpdate;

        probe = new LogProbe(log);
        if (config.EnableLogProbe) probe.Start(chat);

        var ver = typeof(TreasureLedgerPlugin).Assembly.GetName().Version;
        chat.Print($"[TreasureLedger] v{ver} 已加载　输入「{config.QueryCommand}」查询，「{config.ResetCommand}」重置");
        var cmdReady = commandExecutor.TryInitialize();


        chat.Print($"[TreasureLedger] 原生命令执行：{(cmdReady ? "已就绪" : "未就绪")}");

        chat.Print($"[TreasureLedger] 自动投掷：{(rollReady ? "已就绪" : "未就绪")}　历史目录：{history.Directory_}");
        log.Information("[TreasureLedger] 加载完成 v{Ver}，投掷就绪={Ready}", ver?.ToString() ?? "?", rollReady);
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        windowSystem.RemoveAllWindows();

        // 未结束的行程也要落盘
        OnRunEnded();

        framework.Update -= OnFrameworkUpdate;

        capture.LootReceived -= OnLoot;
        capture.RollNotified -= OnRollNotified;
        capture.GilReceived -= OnGil;
        capture.LogSeen -= OnLogSeen;
        capture.Dispose();

        chatCommands.Dispose();
        tracker.Dispose();
        sender.Dispose();
        roller.Dispose();
        dutyExiter.Dispose();
        commandExecutor.Dispose();
        history.Dispose();
        recruit.Dispose();
        probe.Stop(chat);
        probe.Dispose();

        log.Information("[TreasureLedger] 已卸载");
    }

    private void ToggleMain() => mainWindow.Toggle();

    private void SaveConfig() => config.Save(pluginInterface);

    private void ResetLedger()
    {
        ledger.Reset();
        dedup.Reset();
        runGains.Clear();
        treasureContext.Reset();
    }

    /// <summary>行程结束：把本趟收益写入历史。</summary>
    private void OnRunEnded()
    {
        var run = ledger.CurrentRun;
        if (run is null) return;

        // 没收益的空行程不记
        if (runGains.IsEmpty && run.TotalChests == 0 && run.MaxFloor == 0) return;

        try
        {
            run.EndedAt = DateTime.Now;
            var rec = RecordFactory.FromRun(run, runGains);
            history.Append(rec);

            log.Information("[TreasureLedger] 已记录行程 {Rec}", rec.ToString());
            historyWindow.Invalidate();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 记录行程失败");
        }
        finally
        {
            runGains.Clear();
        }
    }

    private void OnRollNotified(uint itemId, int count)
    {
        if (!config.IsItemEnabled(itemId)) return;
        dedup.RegisterRoll(itemId, count);
        log.Debug("[TreasureLedger] 投掷登记 {Item}×{N}", catalog.GetName(itemId), count);
    }

    private void OnLoot(LootEntry entry)
    {
        if (!config.IsItemEnabled(entry.ItemId))
        {
            log.Debug("[TreasureLedger] 已关闭物品，跳过：{Item}", entry.ItemName);
            return;
        }

        var r = dedup.Resolve(entry.ItemId, entry.Count, entry.Source);

        if (r.Count > 0)
        {
            ledger.AddItem(entry.ItemId, r.Count, r.Source);
            runGains.AddItem(entry.ItemId, r.Count, r.Source);
        }

        if (r.HasLeftover)
        {
            ledger.AddItem(entry.ItemId, r.Leftover, r.LeftoverSource);
            runGains.AddItem(entry.ItemId, r.Leftover, r.LeftoverSource);
        }

        if (config.PrintLootLocally)
            chat.Print($"[TreasureLedger] {entry.ItemName} +{r.Count + r.Leftover}");

        if (DateTime.Now - lastPrune > TimeSpan.FromMinutes(5))
        {
            dedup.Prune();
            lastPrune = DateTime.Now;
        }
    }

    private void OnGil(long amount)
    {
        ledger.Gil += amount;
        runGains.Gil += amount;
    }

    /// <summary>每帧检查是否需要自动投掷。</summary>
    private void OnFrameworkUpdate(IFramework fw)
    {
        try
        {
            var cfg = config.AutoRoll;
            if (!cfg.Enabled) return;

            // 挖宝上下文：宝库副本内 或 野外最近有挖宝活动
            // （野外打宝图看门怪时同样会有掉落要 roll）
            var inDungeon = tracker.CurrentDungeon is not null;
            if (cfg.OnlyInTreasureContext
                && !treasureContext.IsTreasureContext(inDungeon, cfg.TreasureContextMinutes))
                return;

            roller.RunOnce(cfg);

            // 自动退出
            var exitCfg = config.AutoExit;
            if (exitCfg.Enabled)
            {
                var run = ledger.CurrentRun;
                var hasBottom = run is not null && run.ReachedBottom;
                if (autoExiter.Tick(exitCfg, tracker.CurrentDungeon is not null, hasBottom))
                    dutyExiter.RequestExit();
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 自动投掷循环异常");
        }
    }

    private void OnLogSeen(uint id, string text)
    {
        recruit.TryParseChat(text);

        // 野外挖宝活动标记（打宝图怪、开宝图箱等）
        if (treasureContext.TryMarkFromLog(text))
            log.Debug("[TreasureLedger] 野外挖宝活动已标记");

        tracker.OnLog(id, text);
    }
}
