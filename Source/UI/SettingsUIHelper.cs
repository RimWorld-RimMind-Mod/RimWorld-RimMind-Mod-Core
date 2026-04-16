using UnityEngine;
using Verse;

namespace RimMind.Core.UI
{
    public static class SettingsUIHelper
    {
        private const float BottomBarHeight = 38f;
        private static readonly Color SectionLineColor = new Color(0.35f, 0.45f, 0.55f, 0.6f);
        private static readonly Color SectionLabelColor = new Color(0.6f, 0.78f, 1f);

        public static void DrawSectionHeader(Listing_Standard listing, string label)
        {
            listing.Gap(8f);
            Rect lineRect = listing.GetRect(1f);
            Widgets.DrawBoxSolid(lineRect, SectionLineColor);
            listing.Gap(4f);
            GUI.color = SectionLabelColor;
            listing.Label(label);
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        public static void DrawCustomPromptSection(Listing_Standard listing, string label, ref string prompt, float height = 80f)
        {
            DrawSectionHeader(listing, label);
            Rect textRect = listing.GetRect(height);
            prompt = Widgets.TextArea(textRect, prompt);
        }

        public static Rect SplitBottomBar(Rect inRect)
        {
            float barY = inRect.yMax - BottomBarHeight;
            return new Rect(inRect.x, barY, inRect.width, BottomBarHeight);
        }

        public static Rect SplitContentArea(Rect inRect)
        {
            return new Rect(inRect.x, inRect.y, inRect.width, inRect.height - BottomBarHeight);
        }

        public static void DrawBottomBar(Rect barRect, System.Action onReset)
        {
            float btnW = 120f;
            float btnH = 30f;
            float btnY = barRect.y + (barRect.height - btnH) / 2f;

            Rect resetBtn = new Rect(barRect.x, btnY, btnW, btnH);
            if (Widgets.ButtonText(resetBtn, "RimMind.Core.Settings.ResetToDefault".Translate()))
                onReset();
        }

    }
}
