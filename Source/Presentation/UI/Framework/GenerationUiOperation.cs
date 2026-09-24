using System;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.UI.Framework
{
    public sealed class GenerationUiOperation
    {
        private readonly RuntimeServiceHub _runtimeHub;
        private readonly string _eventSource;
        private readonly RuntimeServiceScope? _scope;
        private bool _staleRecorded;

        public GenerationUiOperation(
            RuntimeServiceHub runtimeHub,
            RuntimeGenerationToken runtimeToken,
            string eventSource)
        {
            _runtimeHub = runtimeHub ?? throw new ArgumentNullException(nameof(runtimeHub));
            RuntimeToken = runtimeToken;
            _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        }

        private GenerationUiOperation(
            RuntimeServiceHub runtimeHub,
            RuntimeServiceScope scope,
            string eventSource)
            : this(runtimeHub, scope.Token, eventSource)
        {
            _scope = scope;
        }

        public RuntimeGenerationToken RuntimeToken { get; }

        public RuntimeServiceScope Scope =>
            _scope ?? throw new InvalidOperationException(
                "This operation was created from a token and has no captured service scope.");

        public static GenerationUiOperation Capture(
            RuntimeServiceHub runtimeHub,
            string eventSource)
        {
            if (runtimeHub == null)
                throw new ArgumentNullException(nameof(runtimeHub));

            return new GenerationUiOperation(runtimeHub, runtimeHub.Capture(), eventSource);
        }

        public bool CanPublish()
        {
            if (_runtimeHub.IsCurrent(RuntimeToken))
                return true;

            if (!_staleRecorded)
            {
                _staleRecorded = true;
                _runtimeHub.RecordStaleCompletion(_eventSource);
            }

            return false;
        }
    }
}
