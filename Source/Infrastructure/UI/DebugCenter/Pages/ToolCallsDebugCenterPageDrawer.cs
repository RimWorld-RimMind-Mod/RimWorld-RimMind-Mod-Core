using System;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class ToolCallsDebugCenterPageDrawer : DebugTablePageBase
    {
        private readonly ToolCallsDebugTableModelBuilder _modelBuilder;

        public ToolCallsDebugCenterPageDrawer()
            : this(new ToolCallsDebugTableModelBuilder())
        {
        }

        public ToolCallsDebugCenterPageDrawer(ToolCallsDebugTableModelBuilder modelBuilder)
            : base(modelBuilder)
        {
            _modelBuilder = modelBuilder;
        }

        public override IDisposable? Bind(RuntimeServiceScope scope)
        {
            _modelBuilder.Bind(scope.GetOptional<IAIRequestTraceLog>(), scope.Generation);
            return null;
        }
    }
}
