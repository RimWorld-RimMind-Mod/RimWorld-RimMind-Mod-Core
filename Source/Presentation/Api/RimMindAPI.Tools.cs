using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class ToolSet
        {
            private static readonly RuntimeServiceRef<IToolRegistry> ToolRegistries =
                RuntimeServiceRef<IToolRegistry>.Required();
            private static readonly RuntimeServiceRef<IGameMechanismRegistry> MechanismRegistries =
                RuntimeServiceRef<IGameMechanismRegistry>.Required();

            public static IToolRegistry Registry => ToolRegistries.Value;
            public static IGameMechanismRegistry Mechanisms => MechanismRegistries.Value;
        }
    }
}
