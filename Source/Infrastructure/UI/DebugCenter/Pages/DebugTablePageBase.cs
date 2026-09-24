using System;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Presentation.Runtime.Services;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public abstract class DebugTablePageBase : IRuntimeBoundDebugCenterPageDrawer
    {
        private readonly RimMindTableDrawer _tableDrawer = new();
        private readonly IDebugTableModelBuilder _modelBuilder;
        private Vector2 _scrollPosition;
        internal void ScrollToBottom() => _scrollPosition = new Vector2(0, 100000f);

        protected DebugTablePageBase(IDebugTableModelBuilder modelBuilder)
        {
            _modelBuilder = modelBuilder ?? throw new System.ArgumentNullException(nameof(modelBuilder));
        }

        public abstract IDisposable? Bind(RuntimeServiceScope scope);

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            DebugTableModel model = _modelBuilder.Build();
            _tableDrawer.Draw(rect, model, ref _scrollPosition, scope);
        }
    }
}
