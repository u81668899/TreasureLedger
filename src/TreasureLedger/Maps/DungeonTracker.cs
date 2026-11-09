using System;
using System.Linq;
using Dalamud.Plugin.Services;
using TreasureLedger.Ledger;
using TreasureLedger.Loot;

namespace TreasureLedger.Maps;

/// <summary>
/// 副本状态机。
///
/// 统计口径（按需求确认）：
/// - 洞数：每次进入宝库 +1（等同开洞/开魔纹）
/// - 地图数：任务开始 +1，陷阱魔物全清 +1（这两者会重叠，各算各的）
/// - 底数：遇到下底 boss +1（boss 名唯一，不会重复）
/// - 层数：进本记 0 层，每开一个魔纹/大门 +1
/// - 神殿分级：G10/G12/G15 用上中下级，G17-G18 用大梦终梦
/// </summary>
public sealed class DungeonTracker : IDisposable
{
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly SessionLedger ledger;
    private readonly Configuration config;
    private readonly Action resetLedger;

    public uint CurrentTerritory { get; private set; }
    public TreasureDungeon? CurrentDungeon { get; private set; }

    public event Action? StateChanged;

    public DungeonTracker(
        IClientState clientState,
        IPluginLog log,
        SessionLedger ledger,
        Configuration config,
        Action resetLedger)
    {
        this.clientState = clientState;
        this.log = log;
        this.ledger = ledger;
        this.config = config;
        this.resetLedger = resetLedger;

        clientState.TerritoryChanged += OnTerritoryChanged;
        CurrentTerritory = clientState.TerritoryType;
        CurrentDungeon = TreasureDungeons.Find(CurrentTerritory);
    }

    public void Dispose() => clientState.TerritoryChanged -= OnTerritoryChanged;

    private void OnTerritoryChanged(uint territory)
    {
        CurrentTerritory = territory;
        var prev = CurrentDungeon;
        CurrentDungeon = TreasureDungeons.Find(territory);

        if (CurrentDungeon is not null)
        {
            // 进入宝库
            if (prev is null || prev.Name != CurrentDungeon.Name)
            {
                ledger.VaultCount++;   // 洞数
                ledger.MapCount++;     // 地图数（任务开始）
                ledger.StartRun(CurrentDungeon.Name, territory);

                log.Information("[TreasureLedger] 进入 {Name} (TT={TT})　洞数={Vault} 地图数={Map}",
                    CurrentDungeon.Name, territory, ledger.VaultCount, ledger.MapCount);
            }
        }
        else if (prev is not null)
        {
            // 离开宝库
            var run = ledger.EndRun();
            if (run is not null)
            {
                log.Information("[TreasureLedger] 离开 {Name}，最深 {Floor} 层",
                    run.DungeonName, run.MaxFloor);
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>处理关键 LogMessage。</summary>
    public void OnLog(uint logId, string text)
    {
        // ── 先做文本识别（有些事件共用 LogMessageId，只看 ID 分不开）──
        if (!string.IsNullOrEmpty(text) && HandleByText(text))
            return;

        switch (logId)
        {
            // ── 传送魔纹出现 = 开洞，层数推进 ──
            case LogIds.PortalAppeared:
                if (CurrentDungeon is null) return;
                var rec = ledger.AdvanceFloor();
                log.Information("[TreasureLedger] 魔纹出现 → 第 {Floor} 层", rec?.Floor ?? 0);
                StateChanged?.Invoke();
                break;

            // ── 打开了通往XX的大门 = 层数推进 ──
            case LogIds.GateOpenedA:
            case LogIds.GateOpenedB:
                if (CurrentDungeon is null) return;
                var g = ledger.AdvanceFloor();
                log.Information("[TreasureLedger] 大门打开（{Text}）→ 第 {Floor} 层", text, g?.Floor ?? 0);
                StateChanged?.Invoke();
                break;

            // ── 陷阱魔物全清 = 地图数 +1 ──
            case LogIds.TrapMobsCleared:
                if (CurrentDungeon is null) return;
                ledger.MapCount++;
                log.Information("[TreasureLedger] 陷阱魔物清空 → 地图数 {N}", ledger.MapCount);
                StateChanged?.Invoke();
                break;

            // ── 进队重置 ──
            case LogIds.JoinedParty:
            case LogIds.JoinedPartyOf:
                if (config.ResetOnPartyJoin)
                {
                    resetLedger();
                    log.Information("[TreasureLedger] 进队自动重置");
                }
                break;

            case LogIds.RecruitStartedA:
            case LogIds.RecruitStartedB:
                if (config.ResetOnPartyJoin)
                {
                    resetLedger();
                    log.Information("[TreasureLedger] 开招募自动重置");
                }
                break;
        }
    }

    /// <summary>按文本内容识别。返回 true 表示已处理。</summary>
    private bool HandleByText(string text)
    {
        if (CurrentDungeon is null) return false;

        // 下底 boss
        if (TextPatterns.TryGetBottomBoss(text, out var boss))
        {
            OnBottomBoss(boss);
            return true;
        }

        // 大门 / 闸区推进
        if (TextPatterns.TryGetGate(text, out var area))
        {
            var rec = ledger.AdvanceFloor();
            log.Information("[TreasureLedger] {Area} 打开 → 第 {Floor} 层", area, rec?.Floor ?? 0);
            StateChanged?.Invoke();
            return true;
        }

        // 神殿召唤分级
        if (TextPatterns.TryGetShrineTier(text, ledger.UsesDreamNaming, out var tier))
        {
            ledger.AddShrineTier(tier);
            log.Debug("[TreasureLedger] 神殿召唤分级 {Tier}", tier);
            StateChanged?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>下底 boss 出现。</summary>
    public void OnBottomBoss(string bossName)
    {
        if (CurrentDungeon is null) return;
        if (!TreasureDungeons.IsBottomBoss(bossName, out _)) return;

        var cur = ledger.CurrentRun?.Current;
        if (cur is null || cur.ReachedBottom) return;

        ledger.MarkBottom(bossName);
        log.Information("[TreasureLedger] 下底（{Boss}）　底数={N}", bossName, ledger.BottomCount);
        StateChanged?.Invoke();
    }

    /// <summary>神殿召唤，记录分级。</summary>
    public void OnShrineSummon(ShrineTier tier)
    {
        if (CurrentDungeon is null) return;
        ledger.AddShrineTier(tier);
        log.Debug("[TreasureLedger] 神殿召唤 {Tier}", tier);
        StateChanged?.Invoke();
    }

    /// <summary>开了一个宝箱（卫月能直接看到）。</summary>
    public void OnChestOpened()
    {
        if (CurrentDungeon is null) return;
        ledger.AddChest();
        StateChanged?.Invoke();
    }
}
