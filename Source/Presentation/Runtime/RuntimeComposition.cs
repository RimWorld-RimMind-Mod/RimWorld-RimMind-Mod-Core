using System;
using System.Collections.Generic;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime
{
    internal sealed class RuntimeComposition : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _owned;
        private bool _disposed;

        public RuntimeComposition(
            RimMindRuntime runtime,
            RuntimeServiceBuilder services,
            ExtensionRegistryCatalog extensions,
            RuntimeLifetime lifetime,
            IReadOnlyList<IDisposable> owned)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
            Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _owned = owned ?? throw new ArgumentNullException(nameof(owned));
        }

        public RimMindRuntime Runtime { get; }

        public RuntimeServiceBuilder Services { get; }

        public ExtensionRegistryCatalog Extensions { get; }

        public RuntimeLifetime Lifetime { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                Runtime.Shutdown();
            }
            catch (Exception)
            {
                // Retirement must continue so every owned subscription is released.
            }

            for (var index = _owned.Count - 1; index >= 0; index--)
            {
                try
                {
                    _owned[index].Dispose();
                }
                catch (Exception)
                {
                    // One faulty lease cannot keep later leases alive.
                }
            }

            Lifetime.Dispose();
        }
    }

    internal sealed class ActionLease : IDisposable
    {
        private Action? _dispose;

        public ActionLease(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose()
        {
            var dispose = _dispose;
            _dispose = null;
            dispose?.Invoke();
        }
    }
}
