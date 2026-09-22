using System;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    public static class SettingsUIDrawer
    {
        public static void DrawSectionHeader(Listing_Standard listing, string label, string? tooltip = null)
        {
            listing.Gap(10f);
            Rect headerRect = listing.GetRect(32f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.14f, 0.17f, 0.24f, 0.75f));
            Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), new Color(0.4f, 0.7f, 1.0f, 0.9f));

            Rect textRect = new Rect(headerRect.x + 12f, headerRect.y + 2f, headerRect.width - 20f, 28f);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.95f, 1.0f);
            Widgets.Label(textRect, label);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(headerRect, tooltip);
            }
        }

        public static float DrawSectionHeader(Rect canvas, float y, string label, string? tooltip = null)
        {
            float x = canvas.x + 4f;
            float w = canvas.width - 8f;
            Rect headerRect = new Rect(x, y, w, 32f);

            Widgets.DrawBoxSolid(headerRect, new Color(0.14f, 0.17f, 0.24f, 0.75f));
            Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), new Color(0.4f, 0.7f, 1.0f, 0.9f));

            Rect textRect = new Rect(headerRect.x + 12f, headerRect.y + 2f, headerRect.width - 20f, 28f);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.95f, 1.0f);
            Widgets.Label(textRect, label);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(headerRect, tooltip);
            }

            return y + 38f;
        }

        public static void LabelWithTooltip(this Listing_Standard listing, string label, string? tooltip, Color? textColor = null)
        {
            if (textColor.HasValue)
                GUI.color = textColor.Value;

            Rect rect = listing.GetRect(Text.CalcHeight(label, listing.ColumnWidth));
            Widgets.Label(rect, label);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            if (textColor.HasValue)
                GUI.color = Color.white;
        }

        public static float SliderWithTooltip(this Listing_Standard listing, float val, float min, float max, string? tooltip)
        {
            Rect rect = listing.GetRect(22f);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return Widgets.HorizontalSlider(rect, val, min, max);
        }

        public static string TextEntryWithTooltip(this Listing_Standard listing, string text, string? tooltip)
        {
            Rect rect = listing.GetRect(Text.LineHeight);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return Widgets.TextField(rect, text ?? string.Empty);
        }

        public static void DrawCustomPromptSection(
            Listing_Standard listing,
            string label,
            ref string value,
            float pixelHeight,
            string? tooltip = null)
        {
            if (!string.IsNullOrEmpty(tooltip))
                listing.LabelWithTooltip(label, tooltip);
            else
                listing.Label(label);

            Rect rect = listing.GetRect(pixelHeight);
            value = Widgets.TextArea(rect, value ?? string.Empty);
            listing.Gap(6f);
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
            float btnWidth = 160f;
            float btnHeight = 30f;
            float btnX = bottomBar.x + (bottomBar.width - btnWidth) / 2f;
            float btnY = bottomBar.y + (bottomBar.height - btnHeight) / 2f;
            Rect resetRect = new Rect(btnX, btnY, btnWidth, btnHeight);

            if (Widgets.ButtonText(resetRect, "RimMind.UI.ResetToDefaults".Translate()))
            {
                resetAction?.Invoke();
            }
        }
    }
}
