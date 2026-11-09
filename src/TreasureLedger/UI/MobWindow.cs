using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureLedger.Marks;
using TreasureLedger.Mobs;

namespace TreasureLedger.UI;

/// <summary>小怪记录与标记设置页。</summary>
public sealed class MobWindow
{
    private readonly Configuration config;
    private readonly MobTracker tracker;
    private readonly MarkerService marker;
    private readonly Action saveConfig;

    public MobWindow(Configuration config, MobTracker tracker, MarkerService marker, Action saveConfig)
    {
        this.config = config;
        this.tracker = tracker;
        this.marker = marker;
        this.saveConfig = saveConfig;
    }

    public void Draw()
    {
        DrawStats();
        ImGui.Separator();
        DrawMarkSettings();
        ImGui.Separator();
        DrawCatalog();
    }

    private void DrawStats()
    {
        ImGui.TextDisabled("小怪记录（本次会话）");
        ImGui.Spacing();

        var r = tracker.Record;
        ImGui.Text($"顺序怪：{r.OrderSuccess} 击杀 / {r.OrderFail} 逃走");
        ImGui.Text($"金币怪：{r.TreasureSuccess} 击杀 / {r.TreasureFail} 逃走");
        ImGui.Text($"战斗怪：{r.BattleSuccess} 击杀 / {r.BattleFail} 逃走");
        ImGui.TextDisabled($"正在追踪 {tracker.ActiveCount} 只");

        ImGui.Spacing();
        ImGui.TextDisabled("判定：对象消失时已死 = 击杀，未死 = 逃走");
    }

    private void DrawMarkSettings()
    {
        ImGui.TextDisabled("自动标记（本地可见，队友看不到）");
        ImGui.Spacing();

        var cfg = config.Marks;

        var order = cfg.EnableOrderMarks;
        if (ImGui.Checkbox("顺序怪自动标记##m1", ref order))
        {
            cfg.EnableOrderMarks = order;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("洋葱→1 茄子→2 蒜头→3 番茄→4 王后→5");

        var treasure = cfg.EnableTreasureMarks;
        if (ImGui.Checkbox("金币怪自动标记##m2", ref treasure))
        {
            cfg.EnableTreasureMarks = treasure;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("按出现顺序轮转攻击 1-5");

        var waymark = cfg.EnableShrineWaymarks;
        if (ImGui.Checkbox("进神殿自动放标点##m3", ref waymark))
        {
            cfg.EnableShrineWaymarks = waymark;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("十字四点 A/B/C/D");

        ImGui.Spacing();
        if (ImGui.Button("清除所有地面标点##m4"))
            marker.ClearFieldMarkers();

        ImGui.SameLine();
        if (ImGui.Button("清除头顶标记##m5"))
        {
            for (var i = 0; i < 5; i++)
                marker.ClearObjectMark((ObjectMark)i);
        }

        if (marker.LastResult.Length > 0)
            ImGui.TextDisabled("最近：" + marker.LastResult);
    }

    private void DrawCatalog()
    {
        ImGui.TextDisabled("小怪名单（来自 Trigernometry XML）");

        if (!ImGui.BeginChild("##moblist", new Vector2(0, 0), true)) return;

        foreach (MobCategory cat in Enum.GetValues<MobCategory>())
        {
            if (!ImGui.CollapsingHeader($"{CatName(cat)}###cat{(int)cat}")) continue;

            foreach (var d in MobCatalog.All)
            {
                if (d.Category != cat) continue;

                if (cat == MobCategory.Order)
                    ImGui.Text($"  {d.Name}　→ 攻击{d.MarkSlot}");
                else
                    ImGui.Text($"  {d.Name}");
            }
        }

        ImGui.EndChild();
    }

    private static string CatName(MobCategory c) => c switch
    {
        MobCategory.Order => "顺序怪",
        MobCategory.Treasure => "金币怪（盗宝团）",
        MobCategory.Battle => "战斗怪",
        _ => c.ToString(),
    };
}
