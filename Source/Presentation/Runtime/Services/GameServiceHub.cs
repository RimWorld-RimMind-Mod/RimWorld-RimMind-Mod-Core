using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GamePublication
    {
        internal GamePublication(GameServiceSnapshot currentSnapshot, GameServiceSnapshot retiredSnapshot)
        {
            CurrentSnapshot = currentSnapshot;
            RetiredSnapshot = retiredSnapshot;
        }

        public GameServiceSnapshot CurrentSnapshot { get; }

        public GameServiceSnapshot RetiredSnapshot { get; }
    }

    public sealed class GameServiceHub
    {
        private static readonly GameServiceHub SharedInstance = new GameServiceHub();

        private readonly object _publicationLock = new object();
        private readonly Dictionary<Type, long> _optionalMissingGenerations = new Dictionary<Type, long>();
        private readonly Action<string> _optionalMissingDiagnosticSink;
        private readonly ILifecycleEventSink _lifecycleEventSink;
        private GameServiceSnapshot _snapshot;

        internal GameServiceHub(
            Action<string>? optionalMissingDiagnosticSink = null,
            ILifecycleEventSink? lifecycleEventSink = null)
        {
            _optionalMissingDiagnosticSink = optionalMissingDiagnosticSink
                ?? (message => Trace.TraceWarning(message));
            _lifecycleEventSink = lifecycleEventSink ?? ProcessLifecycleEvents.Publisher;
            _snapshot = GameServiceSnapshot.CreateEmpty(0, GameLifecycleState.NeverPublished, null);
        }

        public static GameServiceHub Shared => SharedInstance;

        public long Generation => Volatile.Read(ref _snapshot).Generation;

        internal int OptionalMissingTrackedTypeCount
        {
            get
            {
                lock (_publicationLock)
                {
                    return _optionalMissingGenerations.Count;
                }
            }
        }

        public GameServiceScope Capture()
        {
            return new GameServiceScope(Volatile.Read(ref _snapshot));
        }

        public GamePublication Publish(GameServiceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.State != GameLifecycleState.NeverPublished || snapshot.Generation != 0)
            {
                throw new InvalidOperationException("Only an unpublished game service snapshot can be published.");
            }

            GameServiceSnapshot retired;
            GameServiceSnapshot current;
            lock (_publicationLock)
            {
                retired = _snapshot;
                current = snapshot.WithPublication(
                    checked(retired.Generation + 1),
                    GameLifecycleState.Running,
                    DateTimeOffset.UtcNow);
                Volatile.Write(ref _snapshot, current);
            }

            Emit(new LifecycleEvent(
                LifecycleEventKind.GameServicesPublished,
                gameGeneration: current.Generation,
                serviceCount: current.ServiceCount,
                lifecycleState: current.State.ToString()));
            return new GamePublication(current, retired);
        }

        public GamePublication Stop()
        {
            GameServiceSnapshot retired;
            GameServiceSnapshot current;
            lock (_publicationLock)
            {
                retired = _snapshot;
                current = GameServiceSnapshot.CreateEmpty(
                    checked(retired.Generation + 1),
                    GameLifecycleState.Stopped,
                    DateTimeOffset.UtcNow);
                Volatile.Write(ref _snapshot, current);
            }

            return new GamePublication(current, retired);
        }

        public GameLifecycleDiagnostics GetDiagnostics()
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return new GameLifecycleDiagnostics(
                snapshot.State,
                snapshot.Generation,
                snapshot.ServiceCount,
                snapshot.PublishedAtUtc);
        }

        internal bool RecordOptionalMissing(
            Type serviceType,
            long generation,
            GameLifecycleState state)
        {
            lock (_publicationLock)
            {
                if (_optionalMissingGenerations.TryGetValue(serviceType, out var reportedGeneration)
                    && reportedGeneration >= generation)
                {
                    return false;
                }

                _optionalMissingGenerations[serviceType] = generation;
            }

            var message = $"Optional game service '{serviceType.FullName}' is unavailable at generation {generation} while state is {state}.";
            try
            {
                _optionalMissingDiagnosticSink(message);
            }
            catch (Exception)
            {
                // Diagnostics cannot turn optional resolution into a game failure.
            }

            return true;
        }

        private void Emit(LifecycleEvent lifecycleEvent)
        {
            try
            {
                _lifecycleEventSink.Emit(lifecycleEvent);
            }
            catch (Exception)
            {
                // Diagnostics must never affect game service publication.
            }
        }
    }
}
