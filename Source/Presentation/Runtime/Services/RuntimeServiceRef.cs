using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceRef<T>
        where T : class
    {
        private readonly object _cacheLock = new object();
        private readonly RuntimeServiceHub _hub;
        private readonly bool _required;
        private T? _cached;
        private long _boundGeneration = -1;
        private RuntimeLifecycleState _boundState = RuntimeLifecycleState.NeverPublished;

        internal RuntimeServiceRef(RuntimeServiceHub hub, bool required)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _required = required;
        }

        public static RuntimeServiceRef<T> Required()
        {
            return new RuntimeServiceRef<T>(RuntimeServiceHub.Shared, required: true);
        }

        public static RuntimeServiceRef<T> Optional()
        {
            return new RuntimeServiceRef<T>(RuntimeServiceHub.Shared, required: false);
        }

        public long BoundGeneration
        {
            get
            {
                lock (_cacheLock)
                {
                    return _boundGeneration;
                }
            }
        }

        public T Value
        {
            get
            {
                if (!_required)
                {
                    throw new InvalidOperationException("Value is only valid for a required runtime service reference.");
                }

                RefreshCache();
                lock (_cacheLock)
                {
                    if (_cached == null)
                    {
                        throw new RuntimeServiceUnavailableException(typeof(T), _boundState, _boundGeneration);
                    }

                    return _cached;
                }
            }
        }

        public T? ValueOrDefault
        {
            get
            {
                if (_required)
                {
                    throw new InvalidOperationException("ValueOrDefault is only valid for an optional runtime service reference.");
                }

                RefreshCache();
                T? value;
                long generation;
                RuntimeLifecycleState state;
                lock (_cacheLock)
                {
                    value = _cached;
                    generation = _boundGeneration;
                    state = _boundState;
                }

                if (value == null)
                {
                    _hub.RecordOptionalMissing(typeof(T), generation, state);
                }

                return value;
            }
        }

        public T Resolve(RuntimeServiceScope scope)
        {
            if (!_required)
                throw new InvalidOperationException("Resolve is only valid for a required runtime service reference.");
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            T value = scope.GetRequired<T>();
            Cache(scope, value);
            return value;
        }

        public T? ResolveOptional(RuntimeServiceScope scope)
        {
            if (_required)
                throw new InvalidOperationException("ResolveOptional is only valid for an optional runtime service reference.");
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            T? value = scope.GetOptional<T>();
            Cache(scope, value);
            if (value == null)
                _hub.RecordOptionalMissing(typeof(T), scope.Generation, scope.Snapshot.State);
            return value;
        }

        private void RefreshCache()
        {
            var scope = _hub.Capture();
            T? value = scope.GetOptional<T>();
            Cache(scope, value);
        }

        private void Cache(RuntimeServiceScope scope, T? value)
        {
            lock (_cacheLock)
            {
                if (scope.Generation > _boundGeneration)
                {
                    _cached = value;
                    _boundGeneration = scope.Generation;
                    _boundState = scope.Snapshot.State;
                }
            }
        }
    }
}
