using System;
using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    /// <summary>
    /// Pure mathematical and state evaluator for the HUD RequestOverlay.
    /// Free of direct Unity/Verse GUI dependencies to allow deterministic unit & contract testing.
    /// </summary>
    public static class RequestOverlayLayoutEvaluator
    {
        public const float DragThreshold = 4f;
        public const float DragThresholdSq = DragThreshold * DragThreshold;

        public const float MiniPillWidth = 120f;
        public const float MiniPillHeight = 24f;

        public const float MinWidth = 260f;
        public const float MinHeight = 100f;

        public const float OptionsBarHeight = 24f;
        public const float ResizeHandleSize = 24f;
        public const float TextPadding = 4f;
        public const float EntryLineH = 22f;
        public const float BtnHeight = 22f;
        public const float BtnPadding = 4f;

        /// <summary>
        /// Determines whether mouse movement from the initial down position exceeds the drag threshold (4px).
        /// </summary>
        public static bool IsDragExceeded(Vector2 startPos, Vector2 currentPos)
        {
            float dx = currentPos.x - startPos.x;
            float dy = currentPos.y - startPos.y;
            return (dx * dx + dy * dy) > DragThresholdSq;
        }

        /// <summary>
        /// Clamps window position coordinates within screen bounds, preventing off-screen loss.
        /// </summary>
        public static Vector2 ClampPosition(Vector2 pos, Vector2 windowSize, float screenWidth, float screenHeight)
        {
            float maxX = Mathf.Max(0f, screenWidth - windowSize.x);
            float maxY = Mathf.Max(0f, screenHeight - windowSize.y);
            return new Vector2(
                Mathf.Clamp(pos.x, 0f, maxX),
                Mathf.Clamp(pos.y, 0f, maxY)
            );
        }

        /// <summary>
        /// Evaluates whether the overlay should be in its collapsed mini-pill state.
        /// </summary>
        public static bool ShouldCollapse(
            int pendingCount,
            bool autoHideWhenEmpty,
            bool isExpanded,
            bool isMouseOver,
            bool isDragging,
            bool isResizing)
        {
            // If there are pending requests or auto-hide is disabled, it must NEVER collapse.
            if (pendingCount > 0 || !autoHideWhenEmpty)
                return false;

            // When empty and auto-hide is enabled:
            // If currently expanded, stay expanded as long as user is hovering, dragging, or resizing.
            if (isExpanded)
            {
                if (isMouseOver || isDragging || isResizing)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the bounding rectangle based on collapsed vs expanded state.
        /// </summary>
        public static Rect GetCurrentRect(Vector2 pos, Vector2 expandedSize, bool isCollapsed)
        {
            if (isCollapsed)
            {
                return new Rect(pos.x, pos.y, MiniPillWidth, MiniPillHeight);
            }

            float w = Mathf.Max(MinWidth, expandedSize.x);
            float h = Mathf.Max(MinHeight, expandedSize.y);
            return new Rect(pos.x, pos.y, w, h);
        }
    }
}
