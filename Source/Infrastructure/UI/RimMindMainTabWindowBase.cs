using RimMind.Presentation.UI.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// Base class for all RimMind MainTabWindow subclasses (the bottom-screen
    /// main buttons). Seals DoWindowContents and delegates to DrawContents.
    /// </summary>
    public abstract class RimMindMainTabWindowBase : MainTabWindow
    {
        public override sealed void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            using (var scope = RimMindLayoutScope.Begin(GetType().Name, inRect))
            {
                DrawContents(inRect, scope);
            }
        }

        /// <summary>
        /// Draw the main tab body. Every drawn rect SHOULD be registered with the
        /// scope so conflicts are detected.
        /// </summary>
        protected abstract void DrawContents(Rect inRect, RimMindLayoutScope scope);
    }
}
