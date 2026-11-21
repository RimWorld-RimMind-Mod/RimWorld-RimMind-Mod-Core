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
            DrawSectionHeader(headerRect, label, tooltip);
        }

        public static float DrawSectionHeader(Rect canvas, float y, string label, string? tooltip = null)
        {
            float x = canvas.x + 4f;
            float w = canvas.width - 8f;
            Rect headerRect = new Rect(x, y, w, 32f);
            DrawSectionHeader(headerRect, label, tooltip);
            return y + 38f;
        }

        public static void DrawSectionHeader(Rect headerRect, string label, string? tooltip = null)
        {
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

        public static bool DrawSelectableButton(Rect rect, string label, bool selected, string? tooltip = null)
        {
            bool clicked;
            if (selected)
            {
                Widgets.DrawAtlas(rect, Widgets.ButtonBGAtlasClick);
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, label);
                Text.Anchor = prevAnchor;
                clicked = Widgets.ButtonInvisible(rect);
            }
            else
            {
                clicked = Widgets.ButtonText(rect, label);
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
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

            if (Widgets.ButtonText(resetRect, UiText.Get("RimMind.UI.ResetToDefaults")))
            {
                resetAction?.Invoke();
            }
        }

        /// <summary>
        /// Draws a scrollable settings page with automatic height measurement, dirty-only persistence
        /// (avoiding per-frame disk writes), and an optional bottom reset bar.
        /// </summary>
        public static void DrawScrollableSettingsPage(
            Rect inRect,
            ref Vector2 scrollPos,
            ref float measuredHeight,
            Action<Listing_Standard> drawContent,
            Action? onReset = null,
            Action? onDirtySave = null)
        {
            if (drawContent == null)
                return;

            Rect contentArea = onReset != null ? SplitContentArea(inRect) : inRect;
            float viewHeight = Mathf.Max(measuredHeight, contentArea.height + 1f);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, contentArea.width - 16f), viewHeight);

            bool prevChanged = GUI.changed;
            GUI.changed = false;

            Widgets.BeginScrollView(contentArea, ref scrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            try
            {
                drawContent(listing);
                measuredHeight = listing.CurHeight + 24f;
            }
            finally
            {
                listing.End();
                Widgets.EndScrollView();
            }

            if (onReset != null)
            {
                Rect bottomBar = SplitBottomBar(inRect);
                DrawBottomBar(bottomBar, () =>
                {
                    onReset();
                    GUI.changed = true;
                });
            }

            bool dirty = GUI.changed;
            GUI.changed = prevChanged || dirty;
            if (dirty)
            {
                onDirtySave?.Invoke();
            }
        }

        /// <summary>
        /// Draws a masked secret input field (e.g., API key) with click-to-reveal and Show/Hide toggle.
        /// </summary>
        public static void DrawSecretField(
            Rect row,
            ref string value,
            ref bool isRevealed,
            string emptyPlaceholder,
            string savedFormat,
            string showLabel,
            string hideLabel,
            string? tooltip = null,
            float toggleButtonWidth = 52f)
        {
            float btnW = Mathf.Max(36f, toggleButtonWidth);
            Rect field = new Rect(row.x, row.y, Mathf.Max(0f, row.width - btnW - 4f), row.height);
            Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

            Widgets.DrawBoxSolid(field, new Color(0.04f, 0.05f, 0.08f, 0.6f));
            GUI.color = new Color(0.28f, 0.35f, 0.45f, 0.7f);
            Widgets.DrawBox(field, 1);
            GUI.color = Color.white;

            if (isRevealed)
            {
                value = Widgets.TextField(field, value ?? string.Empty);
            }
            else
            {
                if (string.IsNullOrEmpty(value))
                {
                    GUI.color = new Color(0.6f, 0.65f, 0.7f, 0.65f);
                    Widgets.Label(
                        new Rect(field.x + 8f, field.y + 3f, Mathf.Max(0f, field.width - 16f), field.height),
                        emptyPlaceholder ?? string.Empty);
                    GUI.color = Color.white;
                }
                else
                {
                    string hiddenLabel;
                    try
                    {
                        hiddenLabel = string.Format(savedFormat ?? "Saved ({0} chars)", value.Length);
                    }
                    catch (FormatException)
                    {
                        hiddenLabel = $"{savedFormat} ({value.Length})";
                    }

                    Widgets.Label(
                        new Rect(field.x + 8f, field.y + 3f, Mathf.Max(0f, field.width - 16f), field.height),
                        hiddenLabel);
                }

                if (Widgets.ButtonInvisible(field))
                {
                    isRevealed = true;
                }
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(field, tooltip);
            }

            if (Widgets.ButtonText(toggle, isRevealed ? hideLabel : showLabel))
            {
                isRevealed = !isRevealed;
            }
        }

        /// <summary>
        /// Convenience Listing_Standard wrapper around <see cref="DrawSecretField(Rect, ref string, ref bool, string, string, string, string, string?, float)"/>.
        /// </summary>
        public static void DrawSecretField(
            Listing_Standard listing,
            string label,
            ref string value,
            ref bool isRevealed,
            string? tooltip = null,
            float rowHeight = 26f)
        {
            if (!string.IsNullOrEmpty(label))
            {
                if (!string.IsNullOrEmpty(tooltip))
                    listing.LabelWithTooltip(label, tooltip);
                else
                    listing.Label(label);
            }

            Rect row = listing.GetRect(rowHeight);
            DrawSecretField(
                row,
                ref value,
                ref isRevealed,
                UiText.Get("RimMind.Settings.ApiKey.EmptyPlaceholder", "Click to enter API key..."),
                UiText.Get("RimMind.Settings.ApiKey.Saved", "Saved ({0} chars)"),
                UiText.Get("RimMind.Settings.Show", "Show"),
                UiText.Get("RimMind.Settings.Hide", "Hide"),
                tooltip);
        }
    }
}
