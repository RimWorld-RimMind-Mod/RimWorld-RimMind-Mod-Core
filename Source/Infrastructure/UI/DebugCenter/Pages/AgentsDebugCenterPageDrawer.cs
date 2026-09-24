using System;
using RimMind.Infrastructure.UI.AgentsPage;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class AgentsDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private readonly AgentsPageDrawer _drawer = new();
        internal void ScrollToBottom() => _drawer.ScrollToBottom();

        public IDisposable? Bind(RuntimeServiceScope scope) => null;

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            _drawer.Draw(rect, context.SelectedPawn, scope);
        }
    }
}
