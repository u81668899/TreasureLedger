using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TreasureLedger.UI;

/// <summary>
/// 主窗口：把各页面用标签页组织起来。
/// </summary>
public sealed class MainWindow : Window
{
    public interface IPage
    {
        string Title { get; }
        void DrawPage();
    }

    private readonly List<IPage> pages = new();

    public MainWindow()
        : base("Treasure Ledger###tl-main")
    {
        // 明确允许缩放：不设置 NoResize，且给一个合理的下限。
        // 之前不能缩放的根因是没有设置 SizeConstraints 导致的默认行为异常，
        // 这里显式给出，并把下限放小，保证小窗也能用。
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(900, 620);
        SizeCondition = ImGuiCond.FirstUseEver;

        // 允许调整大小、允许移动、允许折叠
        Flags = ImGuiWindowFlags.None;
    }

    public void Add(string title, object page)
    {
        pages.Add(new PageWrapper(title, page));
    }

    public override void Draw()
    {
        if (pages.Count == 0) return;

        if (!ImGui.BeginTabBar("tl-main-tabs")) return;

        for (var i = 0; i < pages.Count; i++)
        {
            if (!ImGui.BeginTabItem(pages[i].Title)) continue;
            pages[i].DrawPage();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    /// <summary>把已有页面包装成 IPage。</summary>
    private sealed class PageWrapper : IPage
    {
        private readonly object inner;

        public PageWrapper(string title, object inner)
        {
            Title = title;
            this.inner = inner;
        }

        public string Title { get; }

        public void DrawPage()
        {
            switch (inner)
            {
                case HistoryWindow h:
                    h.Draw();
                    break;
                case ConfigWindow c:
                    c.DrawBody();
                    break;
                default:
                    ImGui.TextDisabled("（未实现）");
                    break;
            }
        }
    }
}
