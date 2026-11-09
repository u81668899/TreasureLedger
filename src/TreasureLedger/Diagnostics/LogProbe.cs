using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

namespace TreasureLedger.Diagnostics;

/// <summary>
/// 调试用：把所有 LogMessage 事件原样落到文件，用来确认掉落走哪个 LogMessageId、参数是什么。
/// 定型后可以关掉。
/// </summary>
public sealed class LogProbe : IDisposable
{
    private readonly IPluginLog log;
    private readonly string filePath;
    private readonly object gate = new();
    private StreamWriter? writer;
    private bool enabled;

    public LogProbe(IPluginLog log)
    {
        this.log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN", "pluginConfigs", "TreasureLedger");
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, "logprobe.tsv");
    }

    public string FilePath => filePath;
    public bool Enabled => enabled;

    public void Start(IChatGui chat)
    {
        if (enabled) return;
        chat.LogMessage += OnLogMessage;
        enabled = true;
        log.Information("[TreasureLedger] LogProbe 已启用 -> {Path}", filePath);
    }

    public void Stop(IChatGui chat)
    {
        if (!enabled) return;
        chat.LogMessage -= OnLogMessage;
        enabled = false;
        lock (gate)
        {
            writer?.Flush();
            writer?.Dispose();
            writer = null;
        }
        log.Information("[TreasureLedger] LogProbe 已停用");
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            var sb = new StringBuilder(512);
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append('\t');
            sb.Append(message.LogMessageId).Append('\t');
            sb.Append(message.ParameterCount).Append('\t');

            // 参数：逐个尝试拿到真实值，同时保留 ToString 兜底
            for (var i = 0; i < message.ParameterCount; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(i).Append('=');
                sb.Append(RenderParam(message, i));
            }

            sb.Append('\t');
            try { sb.Append(message.SourceEntity?.Name ?? ""); } catch { }

            // 完整文本（含所有参数已展开的成品句子）
            sb.Append('\t');
            try { sb.Append(message.FormatLogMessageForDebugging().ExtractText().Replace('\n', ' ').Replace('\r', ' ')); }
            catch { }

            Append(sb.ToString());
        }
        catch (Exception ex)
        {
            log.Error(ex, "[TreasureLedger] LogProbe 写入失败");
        }
    }

    private static string RenderParam(ILogMessage message, int index)
    {
        try
        {
            var p = message.Parameters[index];
            // SeStringParameter 通常有 Text / StringValue 之类成员；用反射兼容不同版本
            var t = p.GetType();
            foreach (var name in new[] { "Text", "StringValue", "Value", "String" })
            {
                var pi = t.GetProperty(name);
                if (pi is null) continue;
                var v = pi.GetValue(p);
                if (v is null) continue;
                if (v is string s) return s;
                var str = v.ToString();
                if (!string.IsNullOrEmpty(str)) return str!;
            }
            return p.ToString() ?? "";
        }
        catch
        {
            return "<err>";
        }
    }

    private void Append(string line)
    {
        lock (gate)
        {
            try
            {
                if (writer is null)
                {
                    var isNew = !File.Exists(filePath);
                    writer = new StreamWriter(filePath, append: true, new UTF8Encoding(false));
                    if (isNew)
                        writer.WriteLine("time\tlogId\tparamCount\tparams\tsource\ttext");
                }
                writer.WriteLine(line);
                writer.Flush();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
        enabled = false;
    }
}
