using RimMind.Presentation.UI.Framework;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public readonly struct HubLayoutRects
    {
        public HubLayoutRects(Rect body, Rect header, Rect tabs, Rect content)
        {
            Body = body;
            Header = header;
            Tabs = tabs;
            Content = content;
        }

        public Rect Body { get; }
        public Rect Header { get; }
        public Rect Tabs { get; }
        public Rect Content { get; }
    }

    public static class DebugCenterLayout
    {
        public const float WindowInset = 8f;
        public const float ColumnGap = 10f;

        private static readonly TabbedPageTabModel[] LegacyTabs =
        {
            new("legacy", "Legacy", "RimMind.UI.Hub.Tab.Legacy", true, true, null)
        };

        public static HubLayoutRects CalculateHub(Rect inRect)
        {
            Rect body = inRect.InsetSafe(WindowInset);
            Rect header = new Rect(body.x, body.y, body.width, RimMindUiMetrics.HeaderHeight);
            Rect tabHost = new Rect(
                body.x,
                header.yMax + RimMindUiMetrics.Padding,
                body.width,
                Mathf.Max(1f, body.yMax - header.yMax - RimMindUiMetrics.Padding));
            TabbedPageLayoutResult tabs = TabbedPageLayout.Calculate(tabHost, LegacyTabs);

            return new HubLayoutRects(body, header, tabs.TabBar, tabs.Content);
        }
    }
}
