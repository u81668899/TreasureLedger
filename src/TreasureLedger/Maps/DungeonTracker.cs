using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using TreasureLedger.Ledger;
using TreasureLedger.Loot;
using TreasureLedger.Maps;

namespace TreasureLedger.Maps;

/// <summary>
/// 副本状态机：跟 TerritoryType 变化和关键 LogMessage，维护地图数/洞数/底数。
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
        RefreshDungeon(CurrentTerritory);
    }

    public void Dispose() => clientState.TerritoryChanged -= OnTerritoryChanged;

    private void OnTerritoryChanged(uint territory)
    {
        CurrentTerritory = territory;
        var wasIn = CurrentDungeon is not null;
        RefreshDungeon(territory);

        if (CurrentDungeon is not null)
        {
            // 进入新的宝库 = 地图数 +1
            if (!wasIn || ledger.LastDutyName != CurrentDungeon.Name)
            {
                ledger.MapCount++;
                ledger.VaultCount++;
                ledger.LastDutyName = CurrentDungeon.Name;
                log.Information("[TreasureLedger] 进入宝库 {Name} (TT={TT})，地图数={Map}",
                    CurrentDungeon.Name, territory, ledger.MapCount);
            }
        }

        StateChanged?.Invoke();
    }

    private void RefreshDungeon(uint territory)
        => CurrentDungeon = TreasureDungeons.Find(territory);

    /// <summary>处理关键 LogMessage，推进状态机。</summary>
    public void OnLog(uint logId, string text)
    {
        if (CurrentDungeon is null) return;

        switch (logId)
        {
            case LogIds.TrapMobsCleared:
                // 陷阱魔物全清 = 又过了一张图
                ledger.MapCount++;
                log.Information("[TreasureLedger] 地图数 +1 => {N}", ledger.MapCount);
                StateChanged?.Invoke();
                break;

            case LogIds.PortalAppeared:
                log.Debug("[TreasureLedger] 传送魔纹出现");
                break;

            case LogIds.GateOpenedA:
            case LogIds.GateOpenedB:
                // 层数推进
                log.Debug("[TreasureLedger] 大门打开：{Text}", text);
                break;

            case LogIds.FinalAreaA:
            case LogIds.FinalAreaB:
                // 最终区
                log.Debug("[TreasureLedger] 最终区：{Text}", text);
                break;

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

    /// <summary>下底 boss 出现 = 底数 +1。由外部在识别到 boss 时调用。</summary>
    public void OnBottomReached(string bossName)
    {
        ledger.BottomCount++;
        log.Information("[TreasureLedger] 底数 +1（{Boss}） => {N}", bossName, ledger.BottomCount);
        StateChanged?.Invoke();
    }
}
