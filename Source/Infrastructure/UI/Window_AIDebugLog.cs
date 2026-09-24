using RimMind.Infrastructure.UI.AIRequestsPage;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_AIDebugLog : RimMindWindowBase
    {
        private readonly AIRequestsPageDrawer _traceDrawer = new();

        public override Vector2 InitialSize => new Vector2(860f, 620f);

        public Window_AIDebugLog()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            DrawEmbedded(inRect, scope);
        }

        public void DrawEmbedded(Rect inRect, RimMindLayoutScope? scope = null)
        {
            // Compatibility window: all visible request diagnostics now use IAIRequestTraceLog.
            // IAIDebugLog remains available for legacy clients but is no longer a second UI source.
            _traceDrawer.Draw(inRect, scope);
        }
    }
}
