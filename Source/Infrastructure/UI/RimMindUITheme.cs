using RimMind.Domain.Enums;
using UnityEngine;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// Pure-logic theme constants for RimMind UI.
    /// No Unity GUI dependencies — safe to reference from test projects.
    /// </summary>
    public static class RimMindUITheme
    {
        // ── Spacing ──────────────────────────────────────────────
        public const float Padding = 6f;
        public const float SectionGap = 12f;
        public const float LineHeight = 22f;
        public const float BtnHeight = 24f;
        public const float HeaderHeight = 30f;
        public const float TabHeight = 30f;
        public const float DividerThickness = 1f;

        // ── Colors ───────────────────────────────────────────────
        public static readonly Color ColorHeader = new(0.7f, 0.8f, 1f);
        public static readonly Color ColorSectionTitle = new(0.75f, 0.85f, 1f);
        public static readonly Color ColorKey = new(0.6f, 0.65f, 0.75f);
        public static readonly Color ColorValue = new(0.85f, 0.9f, 1f);
        public static readonly Color ColorMuted = new(0.6f, 0.6f, 0.6f);
        public static readonly Color ColorActive = new(0.4f, 1f, 0.4f);
        public static readonly Color ColorPaused = new(1f, 0.8f, 0.3f);
        public static readonly Color ColorIdle = new(0.7f, 0.7f, 0.7f);
        public static readonly Color ColorError = new(1f, 0.5f, 0.4f);
        public static readonly Color ColorSectionBg = new(0.12f, 0.12f, 0.16f, 0.5f);
        public static readonly Color ColorCardBg = new(0.12f, 0.12f, 0.16f, 0.7f);
        public static readonly Color ColorDivider = new(0.3f, 0.3f, 0.4f, 0.5f);
        public static readonly Color ColorTabActive = new(0.25f, 0.35f, 0.55f, 0.7f);
        public static readonly Color ColorTabHover = new(0.2f, 0.25f, 0.4f, 0.5f);
        public static readonly Color ColorBadgeActiveBg = new(0.15f, 0.35f, 0.15f, 0.6f);
        public static readonly Color ColorBadgePausedBg = new(0.35f, 0.3f, 0.1f, 0.6f);
        public static readonly Color ColorBadgeIdleBg = new(0.25f, 0.25f, 0.25f, 0.6f);
        public static readonly Color ColorBadgeTerminatedBg = new(0.4f, 0.12f, 0.12f, 0.6f);
        public static readonly Color ColorBadgePendingBg = new(0.2f, 0.25f, 0.4f, 0.6f);
        public static readonly Color ColorPending = new(0.5f, 0.7f, 1f);
        public static readonly Color ColorTerminated = new(1f, 0.4f, 0.35f);

        // ── Badge Color Logic ────────────────────────────────────

        /// <summary>
        /// Get badge colors for a given agent state.
        /// </summary>
        public static (Color text, Color bg) GetStateBadgeColors(bool isActive, bool isPaused = false)
        {
            if (isActive) return (ColorActive, ColorBadgeActiveBg);
            if (isPaused) return (ColorPaused, ColorBadgePausedBg);
            return (ColorIdle, ColorBadgeIdleBg);
        }

        /// <summary>
        /// Get badge colors for a given agent state, including Dormant/Terminated/Pending.
        /// </summary>
        public static (Color text, Color bg) GetStateBadgeColors(AgentState state, bool isPendingCreation = false)
        {
            if (isPendingCreation) return (ColorPending, ColorBadgePendingBg);
            return state switch
            {
                AgentState.Active => (ColorActive, ColorBadgeActiveBg),
                AgentState.Paused => (ColorPaused, ColorBadgePausedBg),
                AgentState.Terminated => (ColorTerminated, ColorBadgeTerminatedBg),
                _ => (ColorIdle, ColorBadgeIdleBg)
            };
        }
    }
}
