using System;
using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TreasureLedger.Diagnostics;

/// <summary>
/// Addon 调试工具：把当前可见窗口里的所有按钮及其文本 dump 出来。
/// 用来确认退本按钮到底在哪个 Addon 的哪个节点上。
/// </summary>
public sealed unsafe class AddonDumper
{
    private readonly IPluginLog log;

    public AddonDumper(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>dump 指定 Addon 的所有按钮。</summary>
    public string DumpAddonButtons(string addonName)
    {
        var sb = new StringBuilder();

        try
        {
            var mgr = RaptureAtkUnitManager.Instance();
            if (mgr is null) return "AtkUnitManager 为空";

            var unit = mgr->GetAddonByName(addonName, 1);
            if (unit is null) return $"{addonName} 不存在";

            sb.AppendLine($"[{addonName}] visible={unit->IsVisible} nodeCount={unit->UldManager.NodeListCount}");

            var count = unit->UldManager.NodeListCount;
            for (var i = 0; i < count; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node is null) continue;

                var typeName = node->Type.ToString();
                var nodeId = node->NodeId;

                string text = string.Empty;
                if (node->Type is NodeType.Text)
                {
                    var textNode = (AtkTextNode*)node;
                    text = textNode->NodeText.ToString();
                }
                else if (node->Type is NodeType.Component)
                {
                    var compNode = (AtkComponentNode*)node;
                    if (compNode->Component is not null)
                    {
                        var btn = (AtkComponentButton*)compNode->Component;
                        if (btn->ButtonTextNode is not null)
                            text = btn->ButtonTextNode->NodeText.ToString();
                    }
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                sb.AppendLine($"  [{i}] type={typeName} id={nodeId} text={text.Trim()}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("异常：" + ex.Message);
        }

        return sb.ToString();
    }

    /// <summary>列出当前所有可见 Addon 名。</summary>
    public string DumpVisibleAddons()
    {
        var sb = new StringBuilder();

        try
        {
            var mgr = RaptureAtkUnitManager.Instance();
            if (mgr is null) return "AtkUnitManager 为空";

            // AtkUnitManager 有固定数量的槽位，遍历所有
            var unitMgr = mgr->AllLoadedUnitsList;
            var count = unitMgr.Count;

            for (var i = 0; i < count; i++)
            {
                var unit = unitMgr.Entries[i].Value;
                if (unit is null) continue;
                if (!unit->IsVisible) continue;

                var name = unit->Name.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                sb.AppendLine($"  {name}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("异常：" + ex.Message);
        }

        return sb.ToString();
    }
}
