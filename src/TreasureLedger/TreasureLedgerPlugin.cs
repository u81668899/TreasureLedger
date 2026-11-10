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
using TreasureLedger.Marks;
using TreasureLedger.Mobs;
using TreasureLedger.Recruit;
using TreasureLedger.Storage;
using TreasureLedger.Teleport;
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
    private readonly IPartyList partyList;
    private readonly IGameGui gameGui;

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
    private readonly MobTracker mobTracker;
    private readonly MarkerService markerService;
    private readonly TeleportConfirm teleportConfirm;
    private readonly TreasurePartyDetector treasureParty;

    private readonly WindowSystem windowSystem = new("TreasureLedger");
    private readonly ConfigWindow configWindow;
    private readonly HistoryWindow historyWindow;
    private readonly AutoRollWindow autoRollWindow;
    private readonly MainWindow mainWindow;

    private DateTime lastPrune = DateTime.Now;

    /// <summary>本次进本是否已经放过神殿标点。</summary>
    private bool shrineWaymarksPlaced;

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
        IAddonLifecycle addonLifecycle,
        IPartyList partyList,
        IGameGui gameGui)
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
        this.partyList = partyList;
        this.gameGui = gameGui;

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

        mobTracker = new MobTracker(log, objectTable);
        mobTracker.MobAppeared += OnMobAppeared;
        markerService = new MarkerService(log);

        treasureParty = new TreasurePartyDetector(log, partyList);
        teleportConfirm = new TeleportConfirm(log, addonLifecycle, gameGui);
        teleportConfirm.Confirmed += OnTeleportConfirmed;
        recruit.JoinedRecruit += (title, desc) => treasureParty.OnRecruitInfo(title, desc);
        dutyExiter = new DutyExiter(log, sigScanner);
        tracker = new DungeonTracker(clientState, log, ledger, config, ResetLedger);
        tracker.RunEnded += OnRunEnded;
        tracker.VaultEntered += dg => treasureParty.MarkHardEvidence("进入" + dg.Name);

        capture = new LootCapture(chat, log, catalog);
        capture.LootReceived += OnLoot;
        capture.RollNotified += OnRollNotified;
        capture.GilReceived += OnGil;
        capture.LogSeen += OnLogSeen;

        chatCommands = new ChatCommandListener(
            chat, log, config, ledger, sender, ResetLedger, () => recruit.Current, catalog, () => mobTracker.Record.Summary());

        roller = new AutoRoller(log, sigScanner);
        var rollReady = roller.Probe(config.AutoRoll.CustomSignature);

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
        mainWindow.Add("自动退出", new AutoExitWindow(config, autoExiter, dutyExiter, vaultProbe, SaveConfig));
        mainWindow.Add("小怪与标记", new MobWindow(config, mobTracker, markerService, SaveConfig));
        mainWindow.Add("传送", new TeleportWindow(config, teleportConfirm, treasureParty, SaveConfig));
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

        chat.Print($"[TreasureLedger] 自动投掷：{(rollReady ? "已就绪" : "未就绪：" + roller.UnavailableReason)}");
        chat.Print($"[TreasureLedger] 历史目录：{history.HistoryDirectory}");
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
        mobTracker.MobAppeared -= OnMobAppeared;
        teleportConfirm.Confirmed -= OnTeleportConfirmed;
        teleportConfirm.Dispose();
        mobTracker.Dispose();

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
        mobTracker.Reset();
        treasureMarkCursor = 0;
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

    /// <summary>金币怪标记槽轮转计数。</summary>
    private int treasureMarkCursor;

    /// <summary>小怪出现 → 按配置打本地标记。</summary>
    /// <summary>进神殿时自动放十字四点。</summary>
    private void TickShrineWaymarks()
    {
        var dg = tracker.CurrentDungeon;
        if (dg is null)
        {
            shrineWaymarksPlaced = false;
            return;
        }

        if (shrineWaymarksPlaced) return;
        if (!config.Marks.EnableShrineWaymarks) return;

        var points = ShrineWaymarks.ForDungeon(dg.Name);
        if (points is null) return;

        markerService.PlaceFieldMarkers(points);
        shrineWaymarksPlaced = true;
        log.Information("[TreasureLedger] 已为 {Name} 放置神殿标点", dg.Name);
    }

    private void OnMobAppeared(MobDefinition def, ulong gameObjectId)
    {
        try
        {
            // 挖宝小怪出现 = 硬证据
            treasureParty.MarkHardEvidence("出现" + def.Name);

            if (gameObjectId == 0) return;

            switch (def.Category)
            {
                case MobCategory.Order:
                    if (!config.Marks.EnableOrderMarks) return;
                    // 顺序怪用固定槽位：洋葱=1 茄子=2 蒜头=3 番茄=4 王后=5
                    if (def.MarkSlot is >= 1 and <= 5)
                        markerService.MarkObject(gameObjectId, (ObjectMark)(def.MarkSlot - 1));
                    break;

                case MobCategory.Treasure:
                    if (!config.Marks.EnableTreasureMarks) return;
                    // 金币怪按出现顺序轮转 attack1-5
                    markerService.MarkObject(gameObjectId, (ObjectMark)(treasureMarkCursor % 5));
                    treasureMarkCursor = (treasureMarkCursor + 1) % 5;
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Debug("[TreasureLedger] 自动标记失败：{Msg}", ex.Message);
        }
    }

    /// <summary>同步传送确认的启用状态。</summary>
    private void TickTeleport()
    {
        var cfg = config.Teleport;

        var armed = cfg.EnableAutoConfirm;
        if (armed && cfg.OnlyInTreasureParty)
            armed = treasureParty.IsTreasureParty(cfg.TreasureEvidenceMinutes);

        teleportConfirm.Armed = armed;
    }

    /// <summary>传送被自动确认后提醒。</summary>
    private void OnTeleportConfirmed(string prompt)
    {
        if (!config.Teleport.EnableNotify) return;

        try
        {
            sender.SendTo(ReportChannel.Echo, config, "已自动确认传送：" + prompt);
        }
        catch { }
    }

    private void OnGil(long amount)
    {
        ledger.Gil += amount;
        runGains.Gil += amount;
    }

    /// <summary>上一次做低频工作的时刻。</summary>
    private DateTime lastSlowTick = DateTime.MinValue;

    /// <summary>
    /// 低频工作间隔。
    /// 用户可能用很旧的机器，每帧做大量判断会掉帧。
    /// 这些工作（神殿标点、传送状态、投掷、退出）都不需要每帧精度，
    /// 统一 200ms 一次足够。
    /// </summary>
    private static readonly TimeSpan SlowTickInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>每帧检查。</summary>
    private void OnFrameworkUpdate(IFramework fw)
    {
        try
        {
            // 小怪扫描自己内部有 150ms 限流，直接调用即可
            mobTracker.Tick();

            // 其余工作统一降频
            var now = DateTime.Now;
            if (now - lastSlowTick < SlowTickInterval) return;
            lastSlowTick = now;

            // 进神殿自动放标点（每趟只放一次）
            TickShrineWaymarks();

            // 传送自动确认的启用状态
            TickTeleport();

            // ── 自动投掷 ──
            TickAutoRoll();

            // ── 自动退出 ──
            // 注意：这里必须独立于自动投掷开关。
            // 之前把自动退出写在 `if (!cfg.Enabled) return;` 之后，
            // 导致自动投掷一关就连自动退出也失效了。
            TickAutoExit();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] 主循环异常");
        }
    }

    /// <summary>自动投掷。</summary>
    private void TickAutoRoll()
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
    }

    /// <summary>自动退出。</summary>
    private void TickAutoExit()
    {
        var exitCfg = config.AutoExit;
        if (!exitCfg.Enabled) return;

        var run = ledger.CurrentRun;
        var hasBottom = run is not null && run.ReachedBottom;
        var inDungeon = tracker.CurrentDungeon is not null;

        if (autoExiter.Tick(exitCfg, inDungeon, hasBottom))
            dutyExiter.RequestExit();
    }

    private void OnLogSeen(uint id, string text)
    {
        // ── 性能约束 ──
        // LogMessage 在战斗时每秒可能有上百条。
        // 所以先用最便宜的整数比较把无关日志挡掉，
        // 只对少数几个关心的 id 才做字符串/正则处理。
        switch (id)
        {
            case Loot.LogIds.TrapMobsCleared:
                treasureParty.MarkHardEvidence("陷阱魔物全清");
                tracker.OnLog(id, string.Empty);
                return;

            case Loot.LogIds.PortalAppeared:
                treasureParty.MarkHardEvidence("传送魔纹出现");
                tracker.OnLog(id, string.Empty);
                return;

            case Loot.LogIds.DutyStarted:
            case Loot.LogIds.DutyEnded:
            case Loot.LogIds.GateOpenedA:
            case Loot.LogIds.GateOpenedB:
            case Loot.LogIds.FinalAreaA:
            case Loot.LogIds.FinalAreaB:
            case Loot.LogIds.JoinedParty:
            case Loot.LogIds.JoinedPartyOf:
            case Loot.LogIds.RecruitStartedA:
            case Loot.LogIds.RecruitStartedB:
                // 这些需要文本，但数量有限，正常处理
                break;

            default:
                // 其余日志直接丢弃，不做任何字符串处理
                return;
        }

        recruit.TryParseChat(text);
        treasureContext.TryMarkFromLog(text);
        tracker.OnLog(id, text);
    }
}
