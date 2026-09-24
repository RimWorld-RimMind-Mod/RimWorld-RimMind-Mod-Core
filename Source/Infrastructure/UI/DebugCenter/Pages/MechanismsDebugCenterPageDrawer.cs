using System;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class MechanismsDebugCenterPageDrawer : DebugTablePageBase
    {
        private readonly MechanismsDebugTableModelBuilder _modelBuilder;

        public MechanismsDebugCenterPageDrawer()
            : this(new MechanismsDebugTableModelBuilder())
        {
        }

        public MechanismsDebugCenterPageDrawer(MechanismsDebugTableModelBuilder modelBuilder)
            : base(modelBuilder)
        {
            _modelBuilder = modelBuilder;
        }

        public override IDisposable? Bind(RuntimeServiceScope scope)
        {
            _modelBuilder.Bind(scope.GetOptional<IGameMechanismRegistry>(), scope.Generation);
            return null;
        }
    }
}
