using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceUnavailableException : InvalidOperationException
    {
        public RuntimeServiceUnavailableException(Type serviceType, RuntimeLifecycleState state, long generation)
            : base($"Runtime service '{serviceType.FullName}' is unavailable while state is {state} at generation {generation}.")
        {
            ServiceType = serviceType;
            State = state;
            Generation = generation;
        }

        public Type ServiceType { get; }

        public RuntimeLifecycleState State { get; }

        public long Generation { get; }
    }

    public sealed class GameServiceUnavailableException : InvalidOperationException
    {
        public GameServiceUnavailableException(Type serviceType, GameLifecycleState state, long generation)
            : base($"Game service '{serviceType.FullName}' is unavailable while state is {state} at generation {generation}.")
        {
            ServiceType = serviceType;
            State = state;
            Generation = generation;
        }

        public Type ServiceType { get; }

        public GameLifecycleState State { get; }

        public long Generation { get; }
    }
}
