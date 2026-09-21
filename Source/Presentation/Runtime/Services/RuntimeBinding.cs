using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeBinding : IDisposable
    {
        private readonly object _bindingLock = new object();
        private readonly RuntimeServiceHub _hub;
        private IDisposable? _lease;
        private long _boundGeneration = -1;
        private bool _disposed;

        public RuntimeBinding()
            : this(RuntimeServiceHub.Shared)
        {
        }

        internal RuntimeBinding(RuntimeServiceHub hub)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        }

        public long BoundGeneration
        {
            get
            {
                lock (_bindingLock)
                {
                    return _boundGeneration;
                }
            }
        }

        public void Refresh(Func<RuntimeServiceScope, IDisposable?> bind)
        {
            if (bind == null)
            {
                throw new ArgumentNullException(nameof(bind));
            }

            // Fast-path: avoid Scope allocation if generation hasn't advanced
            if (_boundGeneration >= _hub.Generation)
            {
                return;
            }

            var scope = _hub.Capture();
            lock (_bindingLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(RuntimeBinding));
                }

                if (_boundGeneration >= scope.Generation)
                {
                    return;
                }
            }

            var replacementLease = bind(scope);
            IDisposable? leaseToDispose;
            lock (_bindingLock)
            {
                if (_disposed || _boundGeneration >= scope.Generation)
                {
                    leaseToDispose = replacementLease;
                }
                else
                {
                    leaseToDispose = _lease;
                    _lease = replacementLease;
                    _boundGeneration = scope.Generation;
                }
            }

            leaseToDispose?.Dispose();
        }

        public void Dispose()
        {
            IDisposable? retiredLease;
            lock (_bindingLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                retiredLease = _lease;
                _lease = null;
            }

            retiredLease?.Dispose();
        }
    }
}
