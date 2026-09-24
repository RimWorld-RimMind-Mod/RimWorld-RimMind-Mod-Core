using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Presentation.Runtime.Services;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class RuntimeServiceFrameworkContract
    {
        [Fact]
        public void Runtime_and_game_service_foundation_contracts()
        {
            ContractCaseRunner.Run(
                ("duplicate Bind is rejected", DuplicateBindIsRejected),
                ("explicit Replace updates an existing binding", ExplicitReplaceSucceeds),
                ("Build fails when a required service is missing", MissingRequiredServiceFailsBuild),
                ("built snapshots are insulated from later builder changes", SnapshotIsImmutable),
                ("required and optional resolution have distinct missing semantics", RequiredAndOptionalResolutionDiffer),
                ("each runtime publication advances generation exactly once", RuntimePublicationAdvancesGeneration),
                ("a captured scope resolves one complete generation", ScopeResolvesOneGeneration),
                ("service refs automatically follow the current generation", RefFollowsPublishedGeneration),
                ("failed builds preserve the last good publication", FailedBuildPreservesPublication),
                ("concurrent readers observe only complete old or new graphs", ConcurrentReadersObserveCompleteGraphs),
                ("runtime binding replaces and disposes subscriptions exactly once", RuntimeBindingDisposesExactlyOnce),
                ("runtime binding rejects a replacement produced by reentrant disposal", RuntimeBindingRejectsReentrantReplacement),
                ("runtime binding races never regress generations or leak leases", RuntimeBindingRacesDoNotRegressOrLeak),
                ("retired lifetimes reject and count stale completions", RetiredLifetimeRejectsCompletion),
                ("runtime lifetime coordinates concurrent retirement and disposal", RuntimeLifetimeCoordinatesRetireAndDispose),
                ("runtime refs enforce required and optional access modes", RuntimeRefsEnforceAccessModes),
                ("game refs enforce required and optional access modes", GameRefsEnforceAccessModes),
                ("runtime optional diagnostics are observable once per type generation and bounded", RuntimeOptionalDiagnosticsAreBounded),
                ("game optional diagnostics are observable once per type generation and bounded", GameOptionalDiagnosticsAreBounded),
                ("runtime and game generations advance independently", RuntimeAndGameGenerationsAreIndependent),
                ("game stop publishes an unavailable stopped generation", GameStopPublishesStoppedGeneration),
                ("runtime lifecycle events are ordered safe and preserve publication on failure", RuntimeLifecycleEventsAreSafe),
                ("stale completion events are counted and use approved sources", StaleCompletionEventsAreCounted),
                ("game publication emits a safe lifecycle event", GamePublicationEmitsLifecycleEvent));
        }

        private static void DuplicateBindIsRejected()
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<ILeft>(new Left(1));

            var failure = Assert.Throws<InvalidOperationException>(() => builder.Bind<ILeft>(new Left(2)));

            Assert.Contains(typeof(ILeft).FullName!, failure.Message, StringComparison.Ordinal);
        }

        private static void ExplicitReplaceSucceeds()
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<ILeft>(new Left(1));
            builder.Replace<ILeft>(new Left(2));

            Assert.Equal(2, builder.Build().GetRequired<ILeft>().Version);
            Assert.Throws<InvalidOperationException>(() => new RuntimeServiceBuilder().Replace<ILeft>(new Left(3)));
        }

        private static void MissingRequiredServiceFailsBuild()
        {
            var builder = new RuntimeServiceBuilder();
            builder.Require<ILeft>();

            var failure = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.Contains(typeof(ILeft).FullName!, failure.Message, StringComparison.Ordinal);
        }

        private static void SnapshotIsImmutable()
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<ILeft>(new Left(1));
            var snapshot = builder.Build();

            builder.Replace<ILeft>(new Left(2));

            Assert.Equal(1, snapshot.GetRequired<ILeft>().Version);
            Assert.Equal(2, builder.Build().GetRequired<ILeft>().Version);
            Assert.Equal(RuntimeLifecycleState.NeverPublished, snapshot.State);
            Assert.Equal(0, snapshot.Generation);
            Assert.Null(snapshot.PublishedAtUtc);
        }

        private static void RequiredAndOptionalResolutionDiffer()
        {
            var snapshot = new RuntimeServiceBuilder().Build();

            Assert.Null(snapshot.GetOptional<ILeft>());
            var failure = Assert.Throws<RuntimeServiceUnavailableException>(() => snapshot.GetRequired<ILeft>());
            Assert.Contains(typeof(ILeft).FullName!, failure.Message, StringComparison.Ordinal);
            Assert.Contains(RuntimeLifecycleState.NeverPublished.ToString(), failure.Message, StringComparison.Ordinal);
            Assert.Contains("generation 0", failure.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static void RuntimePublicationAdvancesGeneration()
        {
            var hub = new RuntimeServiceHub();
            Assert.Equal(0, hub.Generation);

            var first = PublishRuntime(hub, CreateRuntimeBuilder(1));
            var second = PublishRuntime(hub, CreateRuntimeBuilder(2));

            Assert.Equal(1, first.CurrentSnapshot.Generation);
            Assert.Equal(2, second.CurrentSnapshot.Generation);
            Assert.Equal(2, hub.Generation);
            Assert.Same(first.CurrentSnapshot, second.RetiredSnapshot);
            Assert.Same(first.CurrentLifetime, second.RetiredLifetime);
        }

        private static void ScopeResolvesOneGeneration()
        {
            var hub = new RuntimeServiceHub();
            PublishRuntime(hub, CreateRuntimeBuilder(7));

            var scope = hub.Capture();

            Assert.Equal(7, scope.GetRequired<ILeft>().Version);
            Assert.Equal(7, scope.GetRequired<IRight>().Version);
            Assert.Equal(scope.Generation, scope.Token.Generation);
        }

        private static void RefFollowsPublishedGeneration()
        {
            var hub = new RuntimeServiceHub();
            PublishRuntime(hub, CreateRuntimeBuilder(1));
            var serviceRef = new RuntimeServiceRef<ILeft>(hub, required: true);

            Assert.Equal(1, serviceRef.Value.Version);
            Assert.Equal(1, serviceRef.BoundGeneration);

            PublishRuntime(hub, CreateRuntimeBuilder(2));

            Assert.Equal(2, serviceRef.Value.Version);
            Assert.Equal(2, serviceRef.BoundGeneration);
        }

        private static void FailedBuildPreservesPublication()
        {
            var hub = new RuntimeServiceHub();
            var publication = PublishRuntime(hub, CreateRuntimeBuilder(4));
            var invalidBuilder = new RuntimeServiceBuilder();
            invalidBuilder.Require<ILeft>();

            var failure = Assert.Throws<InvalidOperationException>(() => invalidBuilder.Build());
            hub.RecordBuildFailure(failure);

            Assert.Same(publication.CurrentSnapshot, hub.Capture().Snapshot);
            Assert.Equal(1, hub.Generation);
            Assert.Equal(RuntimeLifecycleState.Running, hub.GetDiagnostics().State);
            Assert.Contains(nameof(InvalidOperationException), hub.GetDiagnostics().LastBuildFailureSummary!, StringComparison.Ordinal);
        }

        private static void ConcurrentReadersObserveCompleteGraphs()
        {
            var hub = new RuntimeServiceHub();
            PublishRuntime(hub, CreateRuntimeBuilder(0));
            var inconsistencies = new ConcurrentQueue<string>();
            using var start = new ManualResetEventSlim(false);

            var readers = new Task[4];
            for (var readerIndex = 0; readerIndex < readers.Length; readerIndex++)
            {
                readers[readerIndex] = Task.Run(() =>
                {
                    start.Wait();
                    for (var iteration = 0; iteration < 4_000; iteration++)
                    {
                        var scope = hub.Capture();
                        var left = scope.GetRequired<ILeft>().Version;
                        var right = scope.GetRequired<IRight>().Version;
                        if (left != right)
                        {
                            inconsistencies.Enqueue($"generation {scope.Generation}: {left}/{right}");
                        }
                    }
                });
            }

            start.Set();
            for (var version = 1; version <= 100; version++)
            {
                PublishRuntime(hub, CreateRuntimeBuilder(version));
            }

            Task.WaitAll(readers);
            Assert.Empty(inconsistencies);
        }

        private static void RuntimeBindingDisposesExactlyOnce()
        {
            var hub = new RuntimeServiceHub();
            PublishRuntime(hub, CreateRuntimeBuilder(1));
            var binding = new RuntimeBinding(hub);
            var bindCount = 0;
            var firstLease = new CountingLease();
            var secondLease = new CountingLease();

            binding.Refresh(scope =>
            {
                bindCount++;
                return scope.GetRequired<ILeft>().Version == 1 ? firstLease : secondLease;
            });
            binding.Refresh(_ => throw new InvalidOperationException("same generation must not bind again"));

            Assert.Equal(1, bindCount);
            Assert.Equal(0, firstLease.DisposeCount);

            PublishRuntime(hub, CreateRuntimeBuilder(2));
            binding.Refresh(scope =>
            {
                bindCount++;
                Assert.Equal(2, scope.GetRequired<ILeft>().Version);
                return secondLease;
            });

            Assert.Equal(2, bindCount);
            Assert.Equal(1, firstLease.DisposeCount);
            Assert.Equal(0, secondLease.DisposeCount);

            PublishRuntime(hub, CreateRuntimeBuilder(3));
            Assert.Throws<InvalidOperationException>(() => binding.Refresh(_ => throw new InvalidOperationException("bind failed")));
            Assert.Equal(2, binding.BoundGeneration);
            Assert.Equal(0, secondLease.DisposeCount);

            binding.Dispose();
            binding.Dispose();
            Assert.Equal(1, secondLease.DisposeCount);
        }

        private static void RuntimeBindingRejectsReentrantReplacement()
        {
            var hub = new RuntimeServiceHub();
            PublishRuntime(hub, CreateRuntimeBuilder(1));
            var binding = new RuntimeBinding(hub);
            var replacement = new CountingLease();

            binding.Refresh(_ =>
            {
                binding.Dispose();
                return replacement;
            });

            Assert.Equal(-1, binding.BoundGeneration);
            Assert.Equal(1, replacement.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => binding.Refresh(_ => new CountingLease()));
        }

        private static void RuntimeBindingRacesDoNotRegressOrLeak()
        {
            var sameGenerationHub = new RuntimeServiceHub();
            PublishRuntime(sameGenerationHub, CreateRuntimeBuilder(1));
            var sameGenerationBinding = new RuntimeBinding(sameGenerationHub);
            var sameGenerationFirst = new CountingLease();
            var sameGenerationSecond = new CountingLease();
            using var bothBindersEntered = new CountdownEvent(2);
            using var allowSameGenerationBindersToReturn = new ManualResetEventSlim(false);

            Task BindSameGeneration(CountingLease lease)
            {
                return Task.Run(() => sameGenerationBinding.Refresh(_ =>
                {
                    bothBindersEntered.Signal();
                    Assert.True(allowSameGenerationBindersToReturn.Wait(TimeSpan.FromSeconds(5)));
                    return lease;
                }));
            }

            var sameGenerationTaskOne = BindSameGeneration(sameGenerationFirst);
            var sameGenerationTaskTwo = BindSameGeneration(sameGenerationSecond);
            Assert.True(bothBindersEntered.Wait(TimeSpan.FromSeconds(5)));
            allowSameGenerationBindersToReturn.Set();
            Task.WaitAll(sameGenerationTaskOne, sameGenerationTaskTwo);

            Assert.Equal(1, sameGenerationBinding.BoundGeneration);
            Assert.Equal(1, sameGenerationFirst.DisposeCount + sameGenerationSecond.DisposeCount);
            sameGenerationBinding.Dispose();
            Assert.Equal(2, sameGenerationFirst.DisposeCount + sameGenerationSecond.DisposeCount);

            var crossGenerationHub = new RuntimeServiceHub();
            PublishRuntime(crossGenerationHub, CreateRuntimeBuilder(1));
            var crossGenerationBinding = new RuntimeBinding(crossGenerationHub);
            var oldGenerationLease = new CountingLease();
            var newGenerationLease = new CountingLease();
            using var oldBinderEntered = new ManualResetEventSlim(false);
            using var allowOldBinderToReturn = new ManualResetEventSlim(false);

            var oldGenerationTask = Task.Run(() => crossGenerationBinding.Refresh(_ =>
            {
                oldBinderEntered.Set();
                Assert.True(allowOldBinderToReturn.Wait(TimeSpan.FromSeconds(5)));
                return oldGenerationLease;
            }));
            Assert.True(oldBinderEntered.Wait(TimeSpan.FromSeconds(5)));

            PublishRuntime(crossGenerationHub, CreateRuntimeBuilder(2));
            var newGenerationTask = Task.Run(() => crossGenerationBinding.Refresh(_ => newGenerationLease));
            try
            {
                Assert.True(newGenerationTask.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                allowOldBinderToReturn.Set();
            }

            Task.WaitAll(oldGenerationTask, newGenerationTask);
            Assert.Equal(2, crossGenerationBinding.BoundGeneration);
            Assert.Equal(1, oldGenerationLease.DisposeCount);
            Assert.Equal(0, newGenerationLease.DisposeCount);
            crossGenerationBinding.Dispose();
            Assert.Equal(1, newGenerationLease.DisposeCount);
        }

        private static void RetiredLifetimeRejectsCompletion()
        {
            var hub = new RuntimeServiceHub();
            var first = PublishRuntime(hub, CreateRuntimeBuilder(1));
            ICompletionFence firstFence = first.CurrentLifetime!;
            Assert.True(firstFence.TryAcceptCompletion());

            PublishRuntime(hub, CreateRuntimeBuilder(2));

            Assert.True(firstFence.CancellationToken.IsCancellationRequested);
            Assert.False(firstFence.TryAcceptCompletion());
            Assert.False(firstFence.TryAcceptCompletion());
            Assert.Equal(2, first.CurrentLifetime!.StaleCompletionCount);
            Assert.Equal(2, hub.GetDiagnostics().StaleCompletionDiscardCount);
        }

        private static void RuntimeLifetimeCoordinatesRetireAndDispose()
        {
            for (var iteration = 0; iteration < 1_000; iteration++)
            {
                var runtimeId = Guid.NewGuid();
                var lifetime = new RuntimeLifetime(runtimeId, token => token.Generation == 1);
                lifetime.Activate(1);
                var cancellationCount = 0;
                using var registration = lifetime.CancellationToken.Register(
                    () => Interlocked.Increment(ref cancellationCount));
                var failures = new ConcurrentQueue<Exception>();

                Parallel.Invoke(
                    () => CaptureFailure(lifetime.Retire, failures),
                    () => CaptureFailure(lifetime.Dispose, failures));

                lifetime.Retire();
                lifetime.Dispose();
                Assert.Empty(failures);
                Assert.True(lifetime.CancellationToken.IsCancellationRequested);
                Assert.Equal(1, Volatile.Read(ref cancellationCount));
            }

            var activated = new RuntimeLifetime(Guid.NewGuid(), _ => true);
            activated.Activate(1);
            Assert.Throws<InvalidOperationException>(() => activated.Activate(2));
            activated.Dispose();

            var disposed = new RuntimeLifetime(Guid.NewGuid(), _ => true);
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => disposed.Activate(1));
        }

        private static void RuntimeRefsEnforceAccessModes()
        {
            var hub = new RuntimeServiceHub(_ => { });
            PublishRuntime(hub, CreateRuntimeBuilder(5));
            var required = new RuntimeServiceRef<ILeft>(hub, required: true);
            var optional = new RuntimeServiceRef<ILeft>(hub, required: false);
            var missingRequired = new RuntimeServiceRef<IMissingService>(hub, required: true);
            var missingOptional = new RuntimeServiceRef<IMissingService>(hub, required: false);

            Assert.Equal(5, required.Value.Version);
            Assert.Throws<InvalidOperationException>(() => _ = required.ValueOrDefault);
            Assert.Equal(5, optional.ValueOrDefault!.Version);
            Assert.Throws<InvalidOperationException>(() => _ = optional.Value);

            var runningFailure = Assert.Throws<RuntimeServiceUnavailableException>(() => _ = missingRequired.Value);
            Assert.Equal(typeof(IMissingService), runningFailure.ServiceType);
            Assert.Equal(RuntimeLifecycleState.Running, runningFailure.State);
            Assert.Equal(1, runningFailure.Generation);
            Assert.Null(missingOptional.ValueOrDefault);
            Assert.Throws<InvalidOperationException>(() => _ = missingOptional.Value);

            hub.Stop();
            var stoppedFailure = Assert.Throws<RuntimeServiceUnavailableException>(() => _ = required.Value);
            Assert.Equal(typeof(ILeft), stoppedFailure.ServiceType);
            Assert.Equal(RuntimeLifecycleState.Stopped, stoppedFailure.State);
            Assert.Equal(2, stoppedFailure.Generation);
            Assert.Null(optional.ValueOrDefault);
            Assert.Equal(2, optional.BoundGeneration);
        }

        private static void GameRefsEnforceAccessModes()
        {
            var hub = new GameServiceHub(_ => { });
            hub.Publish(CreateGameBuilder(5).Build());
            var required = new GameServiceRef<IGameValue>(hub, required: true);
            var optional = new GameServiceRef<IGameValue>(hub, required: false);
            var missingRequired = new GameServiceRef<IMissingService>(hub, required: true);
            var missingOptional = new GameServiceRef<IMissingService>(hub, required: false);

            Assert.Equal(5, required.Value.Value);
            Assert.Throws<InvalidOperationException>(() => _ = required.ValueOrDefault);
            Assert.Equal(5, optional.ValueOrDefault!.Value);
            Assert.Throws<InvalidOperationException>(() => _ = optional.Value);

            var runningFailure = Assert.Throws<GameServiceUnavailableException>(() => _ = missingRequired.Value);
            Assert.Equal(typeof(IMissingService), runningFailure.ServiceType);
            Assert.Equal(GameLifecycleState.Running, runningFailure.State);
            Assert.Equal(1, runningFailure.Generation);
            Assert.Null(missingOptional.ValueOrDefault);
            Assert.Throws<InvalidOperationException>(() => _ = missingOptional.Value);

            hub.Stop();
            var stoppedFailure = Assert.Throws<GameServiceUnavailableException>(() => _ = required.Value);
            Assert.Equal(typeof(IGameValue), stoppedFailure.ServiceType);
            Assert.Equal(GameLifecycleState.Stopped, stoppedFailure.State);
            Assert.Equal(2, stoppedFailure.Generation);
            Assert.Null(optional.ValueOrDefault);
            Assert.Equal(2, optional.BoundGeneration);
        }

        private static void RuntimeOptionalDiagnosticsAreBounded()
        {
            var messages = new ConcurrentQueue<string>();
            var hub = new RuntimeServiceHub(messages.Enqueue);
            var optional = new RuntimeServiceRef<IMissingService>(hub, required: false);

            PublishRuntime(hub, CreateRuntimeBuilder(1));
            Parallel.For(0, 100, _ => Assert.Null(optional.ValueOrDefault));

            Assert.Single(messages);
            Assert.Contains(typeof(IMissingService).FullName!, messages.Single(), StringComparison.Ordinal);
            Assert.Contains("generation 1", messages.Single(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(RuntimeLifecycleState.Running.ToString(), messages.Single(), StringComparison.Ordinal);
            Assert.DoesNotContain("secret-marker", messages.Single(), StringComparison.Ordinal);
            Assert.Equal(1, hub.OptionalMissingTrackedTypeCount);

            for (var generation = 2; generation <= 20; generation++)
            {
                PublishRuntime(hub, CreateRuntimeBuilder(generation));
                Assert.Null(optional.ValueOrDefault);
                Assert.Null(optional.ValueOrDefault);
            }

            Assert.Equal(20, messages.Count);
            Assert.Equal(1, hub.OptionalMissingTrackedTypeCount);
        }

        private static void GameOptionalDiagnosticsAreBounded()
        {
            var messages = new ConcurrentQueue<string>();
            var hub = new GameServiceHub(messages.Enqueue);
            var optional = new GameServiceRef<IMissingService>(hub, required: false);

            hub.Publish(CreateGameBuilder(1).Build());
            Parallel.For(0, 100, _ => Assert.Null(optional.ValueOrDefault));

            Assert.Single(messages);
            Assert.Contains(typeof(IMissingService).FullName!, messages.Single(), StringComparison.Ordinal);
            Assert.Contains("generation 1", messages.Single(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(GameLifecycleState.Running.ToString(), messages.Single(), StringComparison.Ordinal);
            Assert.Equal(1, hub.OptionalMissingTrackedTypeCount);

            for (var generation = 2; generation <= 20; generation++)
            {
                hub.Publish(CreateGameBuilder(generation).Build());
                Assert.Null(optional.ValueOrDefault);
                Assert.Null(optional.ValueOrDefault);
            }

            Assert.Equal(20, messages.Count);
            Assert.Equal(1, hub.OptionalMissingTrackedTypeCount);
        }

        private static void RuntimeAndGameGenerationsAreIndependent()
        {
            var runtimeHub = new RuntimeServiceHub();
            var gameHub = new GameServiceHub();

            PublishRuntime(runtimeHub, CreateRuntimeBuilder(1));
            PublishRuntime(runtimeHub, CreateRuntimeBuilder(2));
            gameHub.Publish(CreateGameBuilder(10).Build());

            Assert.Equal(2, runtimeHub.Generation);
            Assert.Equal(1, gameHub.Generation);
            Assert.Equal(10, gameHub.Capture().GetRequired<IGameValue>().Value);
        }

        private static void GameStopPublishesStoppedGeneration()
        {
            var hub = new GameServiceHub();
            hub.Publish(CreateGameBuilder(3).Build());

            var stopped = hub.Stop();

            Assert.Equal(2, hub.Generation);
            Assert.Equal(GameLifecycleState.Stopped, stopped.CurrentSnapshot.State);
            Assert.Equal(GameLifecycleState.Stopped, hub.GetDiagnostics().State);
            Assert.Equal(0, hub.GetDiagnostics().ServiceCount);
            Assert.Null(hub.Capture().GetOptional<IGameValue>());
            Assert.Throws<GameServiceUnavailableException>(() => hub.Capture().GetRequired<IGameValue>());
        }

        private static void RuntimeLifecycleEventsAreSafe()
        {
            var events = new ConcurrentQueue<LifecycleEvent>();
            var hub = new RuntimeServiceHub(lifecycleEventSink: new CapturingLifecycleEventSink(events));
            var firstBuilder = CreateRuntimeBuilder(4);
            hub.RecordBuildStarted(firstBuilder.RuntimeId);
            var publication = PublishRuntime(hub, firstBuilder);
            var rejectedBuilder = new RuntimeServiceBuilder();
            rejectedBuilder.Require<ILeft>();
            hub.RecordBuildStarted(rejectedBuilder.RuntimeId);
            var failure = Assert.Throws<InvalidOperationException>(() => rejectedBuilder.Build());
            hub.RecordBuildFailure(rejectedBuilder.RuntimeId, failure);
            hub.RecordRuntimeRetired(publication.CurrentSnapshot.RuntimeId, publication.CurrentSnapshot.Generation);

            Assert.Same(publication.CurrentSnapshot, hub.Capture().Snapshot);
            Assert.Equal(
                new[]
                {
                    LifecycleEventKind.RuntimeBuildStarted,
                    LifecycleEventKind.RuntimePublished,
                    LifecycleEventKind.RuntimeBuildStarted,
                    LifecycleEventKind.RuntimeBuildRejected,
                    LifecycleEventKind.RuntimeRetired
                },
                events.Select(item => item.Kind));
            var rejected = events.Single(item => item.Kind == LifecycleEventKind.RuntimeBuildRejected);
            Assert.Equal(nameof(InvalidOperationException), rejected.ExceptionType);
            Assert.Null(rejected.Source);
            Assert.DoesNotContain("missing", LifecycleEventFormatter.Format(rejected), StringComparison.OrdinalIgnoreCase);
            Assert.All(
                events.Where(item => item.Kind == LifecycleEventKind.RuntimeBuildStarted),
                item => Assert.Equal(RuntimeLifecycleState.Building.ToString(), item.LifecycleState));
        }

        private static void StaleCompletionEventsAreCounted()
        {
            var events = new ConcurrentQueue<LifecycleEvent>();
            var hub = new RuntimeServiceHub(lifecycleEventSink: new CapturingLifecycleEventSink(events));
            var publication = PublishRuntime(hub, CreateRuntimeBuilder(1));

            hub.RecordStaleCompletion("api_request");
            hub.RecordStaleCompletion("secret-user-input");

            Assert.Equal(2, hub.GetDiagnostics().StaleCompletionDiscardCount);
            var staleEvents = events.Where(item => item.Kind == LifecycleEventKind.StaleCompletionDiscarded).ToArray();
            Assert.Equal(2, staleEvents.Length);
            Assert.Equal("api_request", staleEvents[0].Source);
            Assert.Equal(LifecycleEventSources.Unknown, staleEvents[1].Source);
            Assert.All(staleEvents, item => Assert.Equal(publication.CurrentSnapshot.RuntimeId, item.RuntimeId));
        }

        private static void GamePublicationEmitsLifecycleEvent()
        {
            var events = new ConcurrentQueue<LifecycleEvent>();
            var hub = new GameServiceHub(lifecycleEventSink: new CapturingLifecycleEventSink(events));

            var publication = hub.Publish(CreateGameBuilder(9).Build());

            var published = Assert.Single(events);
            Assert.Equal(LifecycleEventKind.GameServicesPublished, published.Kind);
            Assert.Equal(publication.CurrentSnapshot.Generation, published.GameGeneration);
            Assert.Equal(publication.CurrentSnapshot.ServiceCount, published.ServiceCount);
            Assert.Equal(GameLifecycleState.Running.ToString(), published.LifecycleState);
        }

        private static RuntimeServiceBuilder CreateRuntimeBuilder(int version)
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<ILeft>(new Left(version));
            builder.Bind<IRight>(new Right(version));
            builder.Require<ILeft>();
            builder.Require<IRight>();
            return builder;
        }

        private static RuntimePublication PublishRuntime(RuntimeServiceHub hub, RuntimeServiceBuilder builder)
        {
            var snapshot = builder.Build();
            var lifetime = new RuntimeLifetime(snapshot.RuntimeId, hub.IsCurrent, hub.RecordStaleCompletion);
            return hub.Publish(snapshot, lifetime);
        }

        private static GameServiceBuilder CreateGameBuilder(int value)
        {
            var builder = new GameServiceBuilder();
            builder.Bind<IGameValue>(new GameValue(value));
            builder.Require<IGameValue>();
            return builder;
        }

        private static void CaptureFailure(Action action, ConcurrentQueue<Exception> failures)
        {
            try
            {
                action();
            }
            catch (Exception failure)
            {
                failures.Enqueue(failure);
            }
        }

        private interface ILeft
        {
            int Version { get; }
        }

        private interface IRight
        {
            int Version { get; }
        }

        private interface IGameValue
        {
            int Value { get; }
        }

        private interface IMissingService
        {
        }

        private sealed class Left : ILeft
        {
            public Left(int version) => Version = version;
            public int Version { get; }
        }

        private sealed class Right : IRight
        {
            public Right(int version) => Version = version;
            public int Version { get; }
        }

        private sealed class GameValue : IGameValue
        {
            public GameValue(int value) => Value = value;
            public int Value { get; }
        }

        private sealed class CountingLease : IDisposable
        {
            private int _disposeCount;
            public int DisposeCount => Volatile.Read(ref _disposeCount);
            public void Dispose() => Interlocked.Increment(ref _disposeCount);
        }

        private sealed class CapturingLifecycleEventSink : ILifecycleEventSink
        {
            private readonly ConcurrentQueue<LifecycleEvent> _events;

            public CapturingLifecycleEventSink(ConcurrentQueue<LifecycleEvent> events) => _events = events;

            public void Emit(LifecycleEvent lifecycleEvent) => _events.Enqueue(lifecycleEvent);
        }
    }
}
