using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TreasureLedger.UI;

/// <summary>
/// 主窗口：把各页面用标签页组织起来。
/// 每个页面实现 <see cref="IPage"/>。
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
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(900, 620);
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

    /// <summary>把已有的页面对象包装成 IPage。</summary>
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
