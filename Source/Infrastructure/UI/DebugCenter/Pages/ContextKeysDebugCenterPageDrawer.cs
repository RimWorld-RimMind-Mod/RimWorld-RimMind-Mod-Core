using System;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class ContextKeysDebugCenterPageDrawer : DebugTablePageBase
    {
        private readonly ContextKeysDebugTableModelBuilder _modelBuilder;

        public ContextKeysDebugCenterPageDrawer()
            : this(new ContextKeysDebugTableModelBuilder())
        {
        }

        public ContextKeysDebugCenterPageDrawer(ContextKeysDebugTableModelBuilder modelBuilder)
            : base(modelBuilder)
        {
            _modelBuilder = modelBuilder;
        }

        public override IDisposable? Bind(RuntimeServiceScope scope)
        {
            _modelBuilder.Bind(scope.GetOptional<IContextKeyRegistry>(), scope.Generation);
            return null;
        }
    }
}
