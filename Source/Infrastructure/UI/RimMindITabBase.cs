using RimMind.Presentation.UI.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// Base class for all RimMind ITab subclasses. Seals FillTab and delegates
    /// to FillTabContents, wrapping the call in a RimMindLayoutScope.
    /// </summary>
    public abstract class RimMindITabBase : ITab
    {
        protected override sealed void FillTab()
        {
            var rect = new Rect(0f, 0f, size.x, size.y);
            using (var scope = RimMindLayoutScope.Begin(GetType().Name, rect))
            {
                FillTabContents(rect, scope);
            }
        }

        /// <summary>
        /// Draw the tab body. Every drawn rect SHOULD be registered with the
        /// scope so conflicts are detected.
        /// </summary>
        protected abstract void FillTabContents(Rect inRect, RimMindLayoutScope scope);
    }
}
