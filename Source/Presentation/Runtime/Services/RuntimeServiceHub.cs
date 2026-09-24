using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimePublication
    {
        internal RuntimePublication(
            RuntimeServiceSnapshot currentSnapshot,
            RuntimeLifetime? currentLifetime,
            RuntimeServiceSnapshot retiredSnapshot,
            RuntimeLifetime? retiredLifetime)
        {
            CurrentSnapshot = currentSnapshot;
            CurrentLifetime = currentLifetime;
            RetiredSnapshot = retiredSnapshot;
            RetiredLifetime = retiredLifetime;
        }

        public RuntimeServiceSnapshot CurrentSnapshot { get; }

        public RuntimeLifetime? CurrentLifetime { get; }

        public RuntimeServiceSnapshot RetiredSnapshot { get; }

        public RuntimeLifetime? RetiredLifetime { get; }
    }

    public sealed class RuntimeServiceHub
    {
        private static readonly RuntimeServiceHub SharedInstance = new RuntimeServiceHub();

        private readonly object _publicationLock = new object();
        private readonly Dictionary<Type, long> _optionalMissingGenerations = new Dictionary<Type, long>();
        private readonly Action<string> _optionalMissingDiagnosticSink;
        private readonly ILifecycleEventSink _lifecycleEventSink;
        private RuntimeHubState _state;

        internal RuntimeServiceHub(
            Action<string>? optionalMissingDiagnosticSink = null,
            ILifecycleEventSink? lifecycleEventSink = null)
        {
            _optionalMissingDiagnosticSink = optionalMissingDiagnosticSink
                ?? (message => Trace.TraceWarning(message));
            _lifecycleEventSink = lifecycleEventSink ?? ProcessLifecycleEvents.Publisher;
            _state = new RuntimeHubState(
                RuntimeServiceSnapshot.CreateEmpty(
                    Guid.Empty,
                    0,
                    RuntimeLifecycleState.NeverPublished,
                    null),
                null,
                null,
                0);
        }

        public static RuntimeServiceHub Shared => SharedInstance;

        public long Generation => Volatile.Read(ref _state).Snapshot.Generation;

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

        public RuntimeServiceScope Capture()
        {
            return new RuntimeServiceScope(Volatile.Read(ref _state).Snapshot);
        }

        public RuntimePublication Publish(
            RuntimeServiceSnapshot snapshot,
            RuntimeLifetime lifetime,
            bool retireReplacedLifetime = true)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (snapshot.State != RuntimeLifecycleState.NeverPublished || snapshot.Generation != 0)
            {
                throw new InvalidOperationException("Only an unpublished runtime service snapshot can be published.");
            }

            if (snapshot.RuntimeId != lifetime.RuntimeId)
            {
                throw new InvalidOperationException("The runtime snapshot and lifetime must have the same runtime id.");
            }

            RuntimeHubState retired;
            RuntimeHubState current;
            lock (_publicationLock)
            {
                retired = _state;
                var generation = checked(retired.Snapshot.Generation + 1);
                lifetime.Activate(generation);
                var publishedSnapshot = snapshot.WithPublication(
                    generation,
                    RuntimeLifecycleState.Running,
                    DateTimeOffset.UtcNow);
                current = new RuntimeHubState(
                    publishedSnapshot,
                    lifetime,
                    retired.LastBuildFailureSummary,
                    retired.StaleCompletionDiscardCount);
                Volatile.Write(ref _state, current);
            }

            if (retireReplacedLifetime)
            {
                retired.Lifetime?.Retire();
            }
            Emit(new LifecycleEvent(
                LifecycleEventKind.RuntimePublished,
                current.Snapshot.RuntimeId,
                runtimeGeneration: current.Snapshot.Generation,
                serviceCount: current.Snapshot.ServiceCount,
                lifecycleState: current.Snapshot.State.ToString()));
            return new RuntimePublication(
                current.Snapshot,
                current.Lifetime,
                retired.Snapshot,
                retired.Lifetime);
        }

        public RuntimePublication Stop(bool retireReplacedLifetime = true)
        {
            RuntimeHubState retired;
            RuntimeHubState current;
            lock (_publicationLock)
            {
                retired = _state;
                var generation = checked(retired.Snapshot.Generation + 1);
                var stoppedSnapshot = RuntimeServiceSnapshot.CreateEmpty(
                    retired.Snapshot.RuntimeId,
                    generation,
                    RuntimeLifecycleState.Stopped,
                    DateTimeOffset.UtcNow);
                current = new RuntimeHubState(
                    stoppedSnapshot,
                    null,
                    retired.LastBuildFailureSummary,
                    retired.StaleCompletionDiscardCount);
                Volatile.Write(ref _state, current);
            }

            if (retireReplacedLifetime)
            {
                retired.Lifetime?.Retire();
            }
            return new RuntimePublication(
                current.Snapshot,
                null,
                retired.Snapshot,
                retired.Lifetime);
        }

        public bool IsCurrent(RuntimeGenerationToken token)
        {
            var state = Volatile.Read(ref _state);
            return state.Snapshot.State == RuntimeLifecycleState.Running
                && state.Snapshot.RuntimeId == token.RuntimeId
                && state.Snapshot.Generation == token.Generation;
        }

        public void RecordBuildStarted(Guid runtimeId)
        {
            Emit(new LifecycleEvent(
                LifecycleEventKind.RuntimeBuildStarted,
                runtimeId,
                runtimeGeneration: Generation,
                lifecycleState: RuntimeLifecycleState.Building.ToString()));
        }

        public void RecordBuildFailure(Exception failure)
        {
            RecordBuildFailure(Capture().Snapshot.RuntimeId, failure);
        }

        public void RecordBuildFailure(Guid runtimeId, Exception failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            var summary = $"{failure.GetType().Name}: {failure.Message}";
            lock (_publicationLock)
            {
                var state = _state;
                Volatile.Write(
                    ref _state,
                    new RuntimeHubState(
                        state.Snapshot,
                        state.Lifetime,
                        summary,
                        state.StaleCompletionDiscardCount));
            }
            var snapshot = Capture().Snapshot;
            Emit(new LifecycleEvent(
                LifecycleEventKind.RuntimeBuildRejected,
                runtimeId,
                runtimeGeneration: snapshot.Generation,
                serviceCount: snapshot.ServiceCount,
                lifecycleState: snapshot.State.ToString(),
                exceptionType: failure.GetType().Name));
        }

        public void RecordStaleCompletion()
        {
            RecordStaleCompletion(LifecycleEventSources.Unknown);
        }

        public void RecordStaleCompletion(string? source)
        {
            RuntimeServiceSnapshot snapshot;
            lock (_publicationLock)
            {
                var state = _state;
                snapshot = state.Snapshot;
                Volatile.Write(
                    ref _state,
                    new RuntimeHubState(
                        state.Snapshot,
                        state.Lifetime,
                        state.LastBuildFailureSummary,
                        checked(state.StaleCompletionDiscardCount + 1)));
            }
            Emit(new LifecycleEvent(
                LifecycleEventKind.StaleCompletionDiscarded,
                snapshot.RuntimeId,
                runtimeGeneration: snapshot.Generation,
                serviceCount: snapshot.ServiceCount,
                lifecycleState: snapshot.State.ToString(),
                source: LifecycleEventSources.Normalize(source)));
        }

        public void RecordRuntimeRetired(Guid runtimeId, long generation)
        {
            Emit(new LifecycleEvent(
                LifecycleEventKind.RuntimeRetired,
                runtimeId,
                runtimeGeneration: generation,
                lifecycleState: RuntimeLifecycleState.Stopped.ToString()));
        }

        public RuntimeLifecycleDiagnostics GetDiagnostics()
        {
            var state = Volatile.Read(ref _state);
            var lifecycleState = state.Snapshot.State == RuntimeLifecycleState.NeverPublished
                && state.LastBuildFailureSummary != null
                    ? RuntimeLifecycleState.Failed
                    : state.Snapshot.State;
            return new RuntimeLifecycleDiagnostics(
                lifecycleState,
                state.Snapshot.Generation,
                state.Snapshot.ServiceCount,
                state.Snapshot.RuntimeId,
                state.Snapshot.PublishedAtUtc,
                state.LastBuildFailureSummary,
                state.StaleCompletionDiscardCount);
        }

        internal bool RecordOptionalMissing(
            Type serviceType,
            long generation,
            RuntimeLifecycleState state)
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

            var message = $"Optional runtime service '{serviceType.FullName}' is unavailable at generation {generation} while state is {state}.";
            try
            {
                _optionalMissingDiagnosticSink(message);
            }
            catch (Exception)
            {
                // Diagnostics cannot turn optional resolution into a runtime failure.
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
                // Diagnostics must never affect publication or completion fencing.
            }
        }

        private sealed class RuntimeHubState
        {
            public RuntimeHubState(
                RuntimeServiceSnapshot snapshot,
                RuntimeLifetime? lifetime,
                string? lastBuildFailureSummary,
                long staleCompletionDiscardCount)
            {
                Snapshot = snapshot;
                Lifetime = lifetime;
                LastBuildFailureSummary = lastBuildFailureSummary;
                StaleCompletionDiscardCount = staleCompletionDiscardCount;
            }

            public RuntimeServiceSnapshot Snapshot { get; }

            public RuntimeLifetime? Lifetime { get; }

            public string? LastBuildFailureSummary { get; }

            public long StaleCompletionDiscardCount { get; }
        }

    }
}
