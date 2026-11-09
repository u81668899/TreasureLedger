using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using TreasureLedger.Ledger;
using TreasureLedger.Report;

namespace TreasureLedger.Commands;

/// <summary>
/// 指令路由：聊天栏里输入自定义指令（默认「挖宝查询」「挖宝重置」）。
/// 走 ICommandManager，不需要 / 前缀。
/// </summary>
public sealed class CommandRouter : IDisposable
{
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly SessionLedger ledger;
    private readonly Action saveConfig;
    private readonly Action resetLedger;

    private readonly List<string> registered = new();

    public CommandRouter(
        ICommandManager commands,
        IChatGui chat,
        IPluginLog log,
        Configuration config,
        SessionLedger ledger,
        Action saveConfig,
        Action resetLedger)
    {
        this.commands = commands;
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.ledger = ledger;
        this.saveConfig = saveConfig;
        this.resetLedger = resetLedger;

        Rebind();
    }

    /// <summary>指令改名后重新注册。</summary>
    public void Rebind()
    {
        UnregisterAll();

        var q = Normalize(config.QueryCommand);
        if (q.Length > 0)
        {
            commands.AddHandler("/" + q.TrimStart('/'), new Dalamud.Game.Command.CommandInfo(OnQuery)
            {
                HelpMessage = "查询本次挖宝统计",
                ShowInHelp = true,
            });
            registered.Add("/" + q.TrimStart('/'));
        }

        var r = Normalize(config.ResetCommand);
        if (r.Length > 0)
        {
            commands.AddHandler("/" + r.TrimStart('/'), new Dalamud.Game.Command.CommandInfo(OnReset)
            {
                HelpMessage = "重置本次挖宝统计",
                ShowInHelp = true,
            });
            registered.Add("/" + r.TrimStart('/'));
        }

        log.Information("[TreasureLedger] 指令已注册：{Cmds}", string.Join(", ", registered));
    }

    private static string Normalize(string s) => (s ?? string.Empty).Trim();

    private void UnregisterAll()
    {
        foreach (var c in registered)
        {
            try { commands.RemoveHandler(c); } catch { }
        }
        registered.Clear();
    }

    private void OnQuery(string command, string args)
    {
        PrintReport();
    }

    private void OnReset(string command, string args)
    {
        resetLedger();
        chat.Print("[TreasureLedger] 已重置本次统计。");
        log.Information("[TreasureLedger] 统计已重置");
    }

    /// <summary>手动触发一次播报。</summary>
    public void PrintReport()
    {
        foreach (var line in ReportFormatter.BuildFullReport(ledger, config.DisabledSet()))
        {
            chat.Print("[TreasureLedger] " + line);
        }
    }

    /// <summary>把统计发到已启用的频道。</summary>
    public void Broadcast(IReadOnlyList<string> lines, Action<string> sender)
    {
        var channels = config.EnabledChannels;
        if (channels.Count == 0) return;

        foreach (var line in lines)
        {
            foreach (var c in channels)
            {
                sender(config.ChannelSlash(c) + line);
            }
        }
    }

    public void Dispose() => UnregisterAll();
}
