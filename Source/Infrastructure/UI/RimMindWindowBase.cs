using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// Base class for all RimMind Window subclasses. Seals DoWindowContents and
    /// delegates to DrawContents, wrapping the call in a RimMindLayoutScope so
    /// every frame's layout conflicts are auto-published to LayoutConflictStore.
    /// Concrete windows MUST override DrawContents and use the scope for every
    /// rect they draw.
    /// </summary>
    public abstract class RimMindWindowBase : Window
    {
        internal bool CaptureReadOnly { get; set; }
        internal int LastRepaintFrame { get; private set; } = -1;

        public override sealed void DoWindowContents(Rect inRect)
        {
            // RimWorld draws a snapshot of WindowStack. A window closed earlier in the
            // same OnGUI pass can therefore receive one final draw after PreClose.
            if (!IsOpen)
                return;

            // Ignore interaction events rather than disabling GUI: disabled controls
            // tint Repaint output and would make captures unlike the normal window.
            if (CaptureReadOnly && Event.current?.type != EventType.Layout && Event.current?.type != EventType.Repaint)
                return;

            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            try
            {
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                using (var scope = RimMindLayoutScope.Begin(GetType().Name, inRect))
                {
                    DrawContents(inRect, scope);
                }
                if (Event.current != null && Event.current.type == EventType.Repaint)
                    LastRepaintFrame = Time.frameCount;
            }
            finally
            {
                GUI.color = previousColor;
                GUI.enabled = previousEnabled;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// Draw the window body. Every drawn rect SHOULD be registered with the
        /// scope (either via scope.Record or by passing the recorder through
        /// RimMindUI overloads) so conflicts are detected.
        /// </summary>
        protected abstract void DrawContents(Rect inRect, RimMindLayoutScope scope);
    }
}
