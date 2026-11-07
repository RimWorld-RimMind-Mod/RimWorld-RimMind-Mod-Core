using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GameServiceRef<T>
        where T : class
    {
        private readonly GameServiceHub _hub;
        private readonly GenerationCachedServiceRef<T> _cache;

        internal GameServiceRef(GameServiceHub hub, bool required)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _cache = new GenerationCachedServiceRef<T>(required, "game");
        }

        public static GameServiceRef<T> Required()
        {
            return new GameServiceRef<T>(GameServiceHub.Shared, required: true);
        }

        public static GameServiceRef<T> Optional()
        {
            return new GameServiceRef<T>(GameServiceHub.Shared, required: false);
        }

        public long BoundGeneration => _cache.BoundGeneration;

        public T Value =>
            _cache.GetRequiredValue<GameLifecycleState>(
                RefreshCache,
                static (serviceType, state, generation) =>
                    new GameServiceUnavailableException(serviceType, state, generation));

        public T? ValueOrDefault =>
            _cache.GetOptionalValue<GameLifecycleState>(
                RefreshCache,
                (serviceType, generation, state) =>
                    _hub.RecordOptionalMissing(serviceType, generation, state));

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
