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
                    if (isSelected)
                    {
                        Widgets.DrawAtlas(tabRect.Rect, Widgets.ButtonBGAtlasClick);
                        TextAnchor prevAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(tabRect.Rect, tab.Label);
                        Text.Anchor = prevAnchor;

                        if (Widgets.ButtonInvisible(tabRect.Rect) && GUI.enabled)
                            nextSelected = tab.Id;
                    }
                    else
                    {
                        if (Widgets.ButtonText(tabRect.Rect, tab.Label) && GUI.enabled)
                            nextSelected = tab.Id;
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
