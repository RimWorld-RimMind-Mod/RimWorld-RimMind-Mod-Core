using System;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.UI.Framework
{
    /// <summary>
    /// Stores operation data built from one runtime snapshot and gates later publication.
    /// It is not a service container: service resolution is confined to the capture factory.
    /// </summary>
    public sealed class GenerationFencedOperation<TState>
    {
        private readonly GenerationUiOperation _publicationFence;

        private GenerationFencedOperation(
            TState state,
            RuntimeGenerationToken token,
            GenerationUiOperation publicationFence)
        {
            State = state;
            Token = token;
            _publicationFence = publicationFence;
        }

        public TState State { get; }

        public RuntimeGenerationToken Token { get; }

        public static GenerationFencedOperation<TState> Capture(
            RuntimeServiceHub hub,
            string eventSource,
            Func<RuntimeServiceScope, TState> captureState)
        {
            if (hub == null)
                throw new ArgumentNullException(nameof(hub));
            if (captureState == null)
                throw new ArgumentNullException(nameof(captureState));

            RuntimeServiceScope scope = hub.Capture();
            return new GenerationFencedOperation<TState>(
                captureState(scope),
                scope.Token,
                new GenerationUiOperation(hub, scope.Token, eventSource));
        }

        public bool CanPublish()
            => _publicationFence.CanPublish();
    }
}
