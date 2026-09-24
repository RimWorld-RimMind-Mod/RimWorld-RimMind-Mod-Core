using System;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GameServiceRef<T>
        where T : class
    {
        private readonly object _cacheLock = new object();
        private readonly GameServiceHub _hub;
        private readonly bool _required;
        private T? _cached;
        private long _boundGeneration = -1;
        private GameLifecycleState _boundState = GameLifecycleState.NeverPublished;

        internal GameServiceRef(GameServiceHub hub, bool required)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _required = required;
        }

        public static GameServiceRef<T> Required()
        {
            return new GameServiceRef<T>(GameServiceHub.Shared, required: true);
        }

        public static GameServiceRef<T> Optional()
        {
            return new GameServiceRef<T>(GameServiceHub.Shared, required: false);
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
                    throw new InvalidOperationException("Value is only valid for a required game service reference.");
                }

                RefreshCache();
                lock (_cacheLock)
                {
                    if (_cached == null)
                    {
                        throw new GameServiceUnavailableException(typeof(T), _boundState, _boundGeneration);
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
                    throw new InvalidOperationException("ValueOrDefault is only valid for an optional game service reference.");
                }

                RefreshCache();
                T? value;
                long generation;
                GameLifecycleState state;
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

        private void RefreshCache()
        {
            var scope = _hub.Capture();
            lock (_cacheLock)
            {
                if (scope.Generation > _boundGeneration)
                {
                    _cached = scope.GetOptional<T>();
                    _boundGeneration = scope.Generation;
                    _boundState = scope.Snapshot.State;
                }
            }
        }
    }
}
