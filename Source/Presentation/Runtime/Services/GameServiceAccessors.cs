using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;

namespace RimMind.Presentation.Runtime.Services
{
    internal sealed class NpcManagerAccessor : INpcManagerAccessor
    {
        private readonly GameServiceRef<INpcManager> _current =
            GameServiceRef<INpcManager>.Optional();

        public INpcManager? Current => _current.ValueOrDefault;
    }

    internal sealed class AIDebugLogAccessor : IAIDebugLogAccessor
    {
        private readonly GameServiceRef<IAIDebugLog> _current =
            GameServiceRef<IAIDebugLog>.Optional();

        public IAIDebugLog? Current => _current.ValueOrDefault;
    }
}
