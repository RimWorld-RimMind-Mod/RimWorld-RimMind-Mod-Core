using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceRef<T>
        where T : class
    {
        private readonly RuntimeServiceHub _hub;
        private readonly GenerationCachedServiceRef<T> _cache;

        internal RuntimeServiceRef(RuntimeServiceHub hub, bool required)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _cache = new GenerationCachedServiceRef<T>(required, "runtime");
        }

        public static RuntimeServiceRef<T> Required()
        {
            return new RuntimeServiceRef<T>(RuntimeServiceHub.Shared, required: true);
        }

        public static RuntimeServiceRef<T> Optional()
        {
            return new RuntimeServiceRef<T>(RuntimeServiceHub.Shared, required: false);
        }

        public long BoundGeneration => _cache.BoundGeneration;

        public T Value =>
            _cache.GetRequiredValue<RuntimeLifecycleState>(
                RefreshCache,
                static (serviceType, state, generation) =>
                    new RuntimeServiceUnavailableException(serviceType, state, generation));

        public T? ValueOrDefault =>
            _cache.GetOptionalValue<RuntimeLifecycleState>(
                RefreshCache,
                (serviceType, generation, state) =>
                    _hub.RecordOptionalMissing(serviceType, generation, state));

        public T Resolve(RuntimeServiceScope scope)
        {
            _cache.EnsureRequired("Resolve");
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            T value = scope.GetRequired<T>();
            _cache.Cache(scope.Generation, scope.Snapshot.State, value);
            return value;
        }

        public T? ResolveOptional(RuntimeServiceScope scope)
        {
            _cache.EnsureOptional("ResolveOptional");
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            T? value = scope.GetOptional<T>();
            _cache.Cache(scope.Generation, scope.Snapshot.State, value);
            if (value == null)
                _hub.RecordOptionalMissing(typeof(T), scope.Generation, scope.Snapshot.State);
            return value;
        }

        private void RefreshCache()
        {
            var scope = _hub.Capture();
            if (_cache.NeedsRefresh(scope.Generation))
            {
                _cache.Cache(scope.Generation, scope.Snapshot.State, scope.GetOptional<T>());
            }
        }
    }
}
