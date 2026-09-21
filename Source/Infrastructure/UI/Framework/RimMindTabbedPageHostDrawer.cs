using System.Collections.Generic;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.Framework
{
    public sealed class RimMindTabbedPageHostDrawer
    {
        public string DrawTabs(
            Rect root,
            IReadOnlyList<TabbedPageTabModel> tabs,
            string selectedId,
            RimMindLayoutScope? scope)
            => DrawTabs(TabbedPageLayout.Calculate(root, tabs), tabs, selectedId, scope);

        public string DrawTabs(
            TabbedPageLayoutResult layout,
            IReadOnlyList<TabbedPageTabModel> tabs,
            string selectedId,
            RimMindLayoutScope? scope)
        {
            scope?.Record(layout.TabBar, "TabbedPage:TabBar");
            scope?.Record(layout.Content, "TabbedPage:Content");

            string nextSelected = selectedId;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            try
            {
                for (int i = 0; i < layout.TabRects.Count; i++)
                {
                    var tabRect = layout.TabRects[i];
                    var tab = tabs[i];
                    scope?.Record(tabRect.Rect, "TabbedPage:Tab:" + tab.Id);

                    GUI.enabled = previousEnabled && tab.Enabled;
                    bool isSelected = tabRect.Selected;
                    bool isOver = Mouse.IsOver(tabRect.Rect);

                    if (isSelected)
                    {
                        Widgets.DrawBoxSolid(tabRect.Rect, new Color(0.2f, 0.25f, 0.35f, 0.6f));
                        Rect accentLine = new Rect(tabRect.Rect.x, tabRect.Rect.yMax - 2f, tabRect.Rect.width, 2f);
                        Widgets.DrawBoxSolid(accentLine, new Color(0.4f, 0.7f, 1.0f, 0.9f));
                    }

                    GUI.color = isSelected ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1.0f);
                    if (Widgets.ButtonText(tabRect.Rect, tab.Label, drawBackground: !isSelected) && GUI.enabled)
                        nextSelected = tab.Id;

                    if (isOver && tab.Enabled)
                    {
                        Widgets.DrawBoxSolid(tabRect.Rect, new Color(1f, 1f, 1f, 0.08f));
                    }

                    if (!string.IsNullOrEmpty(tab.TooltipKey))
                        TooltipHandler.TipRegion(tabRect.Rect, tab.TooltipKey.Translate());
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.enabled = previousEnabled;
            }

            return nextSelected;
        }
    }
}
