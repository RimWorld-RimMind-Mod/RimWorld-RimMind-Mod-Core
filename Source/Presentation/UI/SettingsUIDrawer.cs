using System;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    public static class SettingsUIDrawer
    {
        public static void DrawSectionHeader(Listing_Standard listing, string label)
        {
            listing.Gap(12f);
            GUI.color = new Color(0.8f, 0.85f, 1f);
            listing.Label(label);
            GUI.color = Color.white;
            listing.Gap(4f);
        }

        public static void DrawCustomPromptSection(Listing_Standard listing, string label, ref string value, float height)
        {
            listing.Label(label);
            value = listing.TextEntry(value, (int)height);
            listing.Gap(4f);
        }

        public static Rect SplitContentArea(Rect inRect)
        {
            return new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 40f);
        }

        public static Rect SplitBottomBar(Rect inRect)
        {
            return new Rect(inRect.x, inRect.yMax - 40f, inRect.width, 40f);
        }

        public static void DrawBottomBar(Rect bottomBar, Action resetAction)
        {
            Widgets.DrawBoxSolid(bottomBar, new Color(0.15f, 0.15f, 0.15f, 0.8f));
            if (Widgets.ButtonText(new Rect(bottomBar.x + 10f, bottomBar.y + 6f, 120f, 28f), "RimMind.UI.ResetToDefaults".Translate()))
            {
                resetAction?.Invoke();
            }
        }
    }
}
