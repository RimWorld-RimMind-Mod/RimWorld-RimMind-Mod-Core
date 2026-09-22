using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// Unified UI drawing utilities for RimMind windows.
    /// Delegates constants and color logic to <see cref="RimMindUITheme"/>.
    /// Every y argument and returned next Y is absolute in the current GUI coordinate system,
    /// not an offset from canvas.y. Scroll-view callers use the content rect's coordinates.
    /// </summary>
    public static class RimMindUI
    {
        // ── Spacing (delegated to Theme) ────────────────────────
        public const float Padding = RimMindUITheme.Padding;
        public const float SectionGap = RimMindUITheme.SectionGap;
        public const float LineHeight = RimMindUITheme.LineHeight;
        public const float BtnHeight = RimMindUITheme.BtnHeight;
        public const float HeaderHeight = RimMindUITheme.HeaderHeight;
        public const float TabHeight = RimMindUITheme.TabHeight;
        public const float DividerThickness = RimMindUITheme.DividerThickness;

        // ── Colors (delegated to Theme) ─────────────────────────
        public static readonly Color ColorHeader = RimMindUITheme.ColorHeader;
        public static readonly Color ColorSectionTitle = RimMindUITheme.ColorSectionTitle;
        public static readonly Color ColorKey = RimMindUITheme.ColorKey;
        public static readonly Color ColorValue = RimMindUITheme.ColorValue;
        public static readonly Color ColorMuted = RimMindUITheme.ColorMuted;
        public static readonly Color ColorActive = RimMindUITheme.ColorActive;
        public static readonly Color ColorPaused = RimMindUITheme.ColorPaused;
        public static readonly Color ColorIdle = RimMindUITheme.ColorIdle;
        public static readonly Color ColorError = RimMindUITheme.ColorError;
        public static readonly Color ColorSectionBg = RimMindUITheme.ColorSectionBg;
        public static readonly Color ColorCardBg = RimMindUITheme.ColorCardBg;
        public static readonly Color ColorDivider = RimMindUITheme.ColorDivider;
        public static readonly Color ColorTabActive = RimMindUITheme.ColorTabActive;
        public static readonly Color ColorTabHover = RimMindUITheme.ColorTabHover;
        public static readonly Color ColorBadgeActiveBg = RimMindUITheme.ColorBadgeActiveBg;
        public static readonly Color ColorBadgePausedBg = RimMindUITheme.ColorBadgePausedBg;
        public static readonly Color ColorBadgeIdleBg = RimMindUITheme.ColorBadgeIdleBg;
        public static readonly Color ColorBadgeTerminatedBg = RimMindUITheme.ColorBadgeTerminatedBg;
        public static readonly Color ColorBadgePendingBg = RimMindUITheme.ColorBadgePendingBg;
        public static readonly Color ColorPending = RimMindUITheme.ColorPending;
        public static readonly Color ColorTerminated = RimMindUITheme.ColorTerminated;

        // ── Badge Color Logic (delegated to Theme) ──────────────

        public static (Color text, Color bg) GetStateBadgeColors(bool isActive, bool isPaused = false)
            => RimMindUITheme.GetStateBadgeColors(isActive, isPaused);

        public static (Color text, Color bg) GetStateBadgeColors(AgentState state, bool isPendingCreation = false)
            => RimMindUITheme.GetStateBadgeColors(state, isPendingCreation);

        // ── Section Header ───────────────────────────────────────

        /// <summary>
        /// Draw a section header with an underline divider. Returns new Y.
        /// </summary>
        public static float DrawSectionHeader(Rect canvas, float y, string label)
            => DrawSectionHeader(canvas, y, label, null);

        /// <summary>
        /// Draw a section header with an underline divider. Returns new Y.
        /// When <paramref name="recorder"/> is non-null, the header label rect is recorded for layout diagnostics.
        /// </summary>
        public static float DrawSectionHeader(Rect canvas, float y, string label, LayoutTraceRecorder? recorder)
        {
            float x = canvas.x + Padding;
            float w = canvas.width - Padding * 2;

            Rect headerRect = new Rect(x, y, w, LineHeight);
            GUI.color = ColorSectionTitle;
            Text.Font = GameFont.Small;
            Widgets.Label(headerRect, label);
            GUI.color = Color.white;
            y += LineHeight;

            Widgets.DrawLine(new Vector2(x, y), new Vector2(x + w, y), ColorDivider, DividerThickness);
            y += SectionGap * 0.5f;

            recorder?.Record(headerRect, $"SectionHeader:{label}", nameof(DrawSectionHeader));
            return y;
        }

        // ── Key-Value Row ────────────────────────────────────────

        /// <summary>
        /// Draw a "Key: Value" row. Key in muted color, Value in bright color. Returns new Y.
        /// </summary>
        public static float DrawKeyValueRow(Rect canvas, float y, string key, string value)
            => DrawKeyValueRow(canvas, y, key, value, null);

        /// <summary>
        /// Draw a "Key: Value" row. Key in muted color, Value in bright color. Returns new Y.
        /// When <paramref name="recorder"/> is non-null, the row rect is recorded for layout diagnostics.
        /// </summary>
        public static float DrawKeyValueRow(Rect canvas, float y, string key, string value, LayoutTraceRecorder? recorder)
        {
            float x = canvas.x + Padding;
            float w = canvas.width - Padding * 2;

            Rect rowRect = new Rect(x, y, w, LineHeight);
            if (!string.IsNullOrEmpty(key))
            {
                string keyText = key + ": ";
                Vector2 keySize = Text.CalcSize(keyText);
                GUI.color = ColorKey;
                Widgets.Label(new Rect(x, y, keySize.x, LineHeight), keyText);

                GUI.color = ColorValue;
                Widgets.Label(new Rect(x + keySize.x, y, w - keySize.x, LineHeight), value);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = ColorValue;
                Widgets.Label(rowRect, value);
                GUI.color = Color.white;
            }

            recorder?.Record(rowRect, $"KV:{key}", nameof(DrawKeyValueRow));
            return y + LineHeight + Padding * 0.5f;
        }

        // ── Status Badge ─────────────────────────────────────────

        /// <summary>
        /// Draw a status badge with colored background. Returns new Y.
        /// </summary>
        public static float DrawStatusBadge(Rect canvas, float y, string label, Color textColor, Color bgColor)
            => DrawStatusBadge(canvas, y, label, textColor, bgColor, null);

        /// <summary>
        /// Draw a status badge with colored background. Returns new Y.
        /// When <paramref name="recorder"/> is non-null, the badge rect is recorded for layout diagnostics.
        /// </summary>
        public static float DrawStatusBadge(Rect canvas, float y, string label, Color textColor, Color bgColor, LayoutTraceRecorder? recorder)
        {
            float x = canvas.x + Padding;
            Vector2 textSize = Text.CalcSize(label);
            float badgeW = textSize.x + Padding * 2;
            float badgeH = LineHeight;

            Rect badgeRect = new Rect(x, y, badgeW, badgeH);
            Widgets.DrawBoxSolid(badgeRect, bgColor);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = textColor;
            Widgets.Label(badgeRect, label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            recorder?.Record(badgeRect, $"Badge:{label}", nameof(DrawStatusBadge));
            return y + badgeH + Padding * 0.5f;
        }

        // ── Divider ──────────────────────────────────────────────

        /// <summary>
        /// Draw a horizontal divider line. Returns new Y.
        /// </summary>
        public static float DrawDivider(Rect canvas, float y)
            => DrawDivider(canvas, y, null);

        /// <summary>
        /// Draw a horizontal divider line. Returns new Y.
        /// When <paramref name="recorder"/> is non-null, the divider line rect is recorded for layout diagnostics.
        /// </summary>
        public static float DrawDivider(Rect canvas, float y, LayoutTraceRecorder? recorder)
        {
            float x = canvas.x + Padding;
            float w = canvas.width - Padding * 2;
            Rect dividerRect = new Rect(x, y, w, DividerThickness);
            Widgets.DrawLine(new Vector2(x, y), new Vector2(x + w, y), ColorDivider, DividerThickness);
            recorder?.Record(dividerRect, "Divider", nameof(DrawDivider));
            return y + SectionGap * 0.5f;
        }

        // ── Section Background ───────────────────────────────────

        /// <summary>
        /// Draw a section background rectangle. Returns the content rect (inset by padding).
        /// </summary>
        public static Rect DrawSectionBg(Rect canvas, float y, float height)
        {
            Rect bgRect = new Rect(canvas.x, y, canvas.width, height);
            Widgets.DrawBoxSolid(bgRect, ColorSectionBg);
            return bgRect.ContractedBy(Padding);
        }

        // ── Tab Button ───────────────────────────────────────────

        /// <summary>
        /// Draw a tab button with active/inactive styling. Returns true if clicked.
        /// </summary>
        public static bool DrawTabButton(Rect rect, string label, bool selected)
        {
            if (selected)
            {
                Widgets.DrawAtlas(rect, Widgets.ButtonBGAtlasClick);
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = ColorHeader;
                Widgets.Label(rect, label);
                GUI.color = Color.white;
                Text.Anchor = prevAnchor;
                return Widgets.ButtonInvisible(rect);
            }

            return Widgets.ButtonText(rect, label);
        }
        /// </summary>
        public static bool DrawActionButton(Rect rect, string label)
        {
            return Widgets.ButtonText(rect, label);
        }

        // ── Wrapped Label ────────────────────────────────────────

        /// <summary>
        /// Draw a word-wrapped label and return the next Y, including bottom spacing.
        /// </summary>
        public static float DrawWrappedLabel(Rect canvas, float y, string text, Color color)
            => DrawWrappedLabel(canvas, y, text, color, null);

        /// <summary>
        /// Draw a word-wrapped label and return the next Y, including bottom spacing.
        /// When <paramref name="recorder"/> is non-null, the label rect is recorded for layout diagnostics.
        /// </summary>
        public static float DrawWrappedLabel(Rect canvas, float y, string text, Color color, LayoutTraceRecorder? recorder)
        {
            float x = canvas.x + Padding;
            float w = canvas.width - Padding * 2;

            GUI.color = color;
            float h = Text.CalcHeight(text, w);
            Rect labelRect = new Rect(x, y, w, h);
            Widgets.Label(labelRect, text);
            GUI.color = Color.white;

            recorder?.Record(labelRect, $"Label:{text}", nameof(DrawWrappedLabel));
            return y + h + Padding * 0.5f;
        }

        // ── Empty State ──────────────────────────────────────────

        /// <summary>
        /// Draw a centered empty state message with optional hint. Returns new Y.
        /// </summary>
        public static void DrawEmptyState(Rect rect, string message, string? hint = null)
        {
            float centerY = rect.y + rect.height / 2f;

            GUI.color = ColorMuted;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, centerY - 20f, rect.width, LineHeight), message);

            if (!hint.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                float hintH = Text.CalcHeight(hint, rect.width - 24f);
                Widgets.Label(new Rect(rect.x + 12f, centerY + 4f, rect.width - 24f, hintH), hint);
                Text.Font = GameFont.Small;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ── Window Header ────────────────────────────────────────

        /// <summary>
        /// Draw a window title header. Returns new Y.
        /// </summary>
        public static float DrawWindowHeader(Rect inRect, string title)
        {
            GUI.color = ColorHeader;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return inRect.y + HeaderHeight + Padding;
        }

        // ── Scroll View Helper ───────────────────────────────────

        /// <summary>
        /// Begin a scroll view with standard scrollbar width. Returns (bodyRect, viewRect).
        /// Caller must call Widgets.EndScrollView() when done.
        /// </summary>
        public static (Rect bodyRect, Rect viewRect) BeginScrollView(Rect rect, ref Vector2 scrollPos, float contentHeight)
        {
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentHeight);
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            return (viewRect, viewRect);
        }
    }
}
