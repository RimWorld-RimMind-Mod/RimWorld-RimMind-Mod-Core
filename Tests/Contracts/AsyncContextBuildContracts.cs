using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Flywheel;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Cache;
using RimMind.Presentation.Context;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class AsyncContextBuildContracts
    {
        [Fact]
        public async Task Snapshot_awaits_provider_and_preserves_layer_order_and_scenario_history()
        {
            var runtime = new ContextRuntime();
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Keys.Register(new ContextProviderDef("identity", ContextLayer.L0_Static, 1,
                (_, _) => completion.Task));
            runtime.Keys.Register(new KeyMeta("sensor", ContextLayer.L5_Sensor, 1,
                _ => new List<ContextEntry> { new() { SourceKey = "sensor", Content = "danger" } }, "test"));
            runtime.History.AddTurn("pawn", "earlier question", "earlier answer", "context-test");
            runtime.History.AddTurn("pawn", "other scenario", "unrelated answer", "other");

            var pending = runtime.Engine.BuildSnapshotFromEnvelopeAsync("pawn", "current query",
                maxTokens: 456, temperature: 0.2f, scenarioId: "context-test");
            Assert.False(pending.IsCompleted);
            completion.SetResult("colonist identity");
            var snapshot = Assert.IsType<ContextSnapshot>(await pending.WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.Equal(new[] { "L0", "L5" }, snapshot.Messages.Take(2).Select(message => message.LayerTag));
            Assert.Contains("colonist identity", snapshot.Messages[0].Content);
            Assert.Equal(new[] { "earlier question", "earlier answer", "current query" },
                snapshot.Messages.Skip(2).Select(message => message.Content));
            Assert.Equal(456, snapshot.MaxTokens);
            Assert.Equal(0.2f, snapshot.Temperature);
            Assert.True(snapshot.EstimatedTokens > 0);
        }

        [Fact]
        public async Task Cached_provider_recomputes_after_invalidation_event()
        {
            var runtime = new ContextRuntime();
            var calls = 0;
            runtime.Keys.Register(new ContextProviderDef("health", ContextLayer.L3_State, 1,
                (_, _) => Task.FromResult<string?>($"health-{++calls}"),
                stalenessTicks: 1000, invalidationTriggers: new[] { "Perception" }));

            await runtime.Build();
            var cached = await runtime.Build();
            Assert.Contains("health-1", cached!.Messages[0].Content);
            runtime.Bus.Publish(new PerceptionEvent("pawn", 1, "health", "changed"));
            var refreshed = await runtime.Build();
            Assert.Contains("health-2", refreshed!.Messages[0].Content);
            Assert.Equal(2, calls);
        }

        [Fact]
        public void Npc_invalidation_clears_history_and_diff_state()
        {
            var runtime = new ContextRuntime();
            runtime.History.AddTurn("pawn", "old question", "old answer", "context-test");
            runtime.Diffs.AddDiff("pawn", "health", "old", "new", ContextLayer.L3_State);
            runtime.Diffs.SetKeyLastValue("pawn", "health", "new");
            runtime.Engine.InvalidateNpc("pawn");
            Assert.Empty(runtime.History.GetHistory("pawn", 6, "context-test"));
            Assert.False(runtime.Diffs.TryGetDiffStore("pawn", out _));
            Assert.False(runtime.Diffs.TryGetKeyLastValues("pawn", out _));
        }

        [Fact]
        public async Task Provider_failure_does_not_discard_other_layers()
        {
            var runtime = new ContextRuntime();
            runtime.Keys.Register(new ContextProviderDef("identity", ContextLayer.L0_Static, 1,
                (_, _) => Task.FromResult<string?>("identity")));
            runtime.Keys.Register(new ContextProviderDef("failed", ContextLayer.L3_State, 1,
                (_, _) => Task.FromException<string?>(new InvalidOperationException("provider unavailable"))));
            var snapshot = await runtime.Build();
            Assert.Contains("identity", snapshot!.Messages[0].Content);
            Assert.DoesNotContain(snapshot.Messages, message => message.LayerTag == "L3");
            Assert.Contains(runtime.Log.Warnings, warning => warning.Contains("layer=L3", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Cancellation_reaches_pending_provider_and_propagates_to_caller()
        {
            var runtime = new ContextRuntime();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Keys.Register(new ContextProviderDef("pending", ContextLayer.L3_State, 1,
                async (_, ct) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                    return "unreachable";
                }));
            using var cancellation = new CancellationTokenSource();
            var pending = runtime.Engine.BuildSnapshotFromEnvelopeAsync("pawn", "query", ct: cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public async Task Budget_trimming_preserves_current_input()
        {
            var runtime = new ContextRuntime();
            runtime.Keys.Register(new ContextProviderDef("sensor", ContextLayer.L5_Sensor, 1,
                (_, _) => Task.FromResult<string?>(new string('x', 40000))));
            var snapshot = Assert.IsType<ContextSnapshot>(await runtime.Engine.BuildSnapshotFromEnvelopeAsync(
                "pawn", "current query"));
            Assert.Contains(snapshot.Messages, message => message.Role == "user" && message.Content == "current query");
            Assert.True(snapshot.EstimatedTokens < 10000);
            Assert.All(snapshot.Messages.Where(message => message.LayerTag == "L5"),
                message => Assert.True(message.Content.Length < 40000));
        }

        [Fact]
        public async Task Skipped_state_layer_does_not_invoke_its_provider()
        {
            var runtime = new ContextRuntime();
            var stateCalls = 0;
            runtime.Keys.Register(new ContextProviderDef("state", ContextLayer.L3_State, 1,
                (_, _) => { stateCalls++; return Task.FromResult<string?>("state"); }));

            var snapshot = Assert.IsType<ContextSnapshot>(await runtime.Engine.BuildSnapshotFromEnvelopeAsync(
                "pawn", "current query", skipLayers: new HashSet<string> { "L3" }));

            Assert.Equal(0, stateCalls);
            Assert.DoesNotContain(snapshot.Messages, message => message.LayerTag == "L3");
            Assert.Contains(snapshot.Messages, message => message.Content == "current query");
        }

        private sealed class ContextRuntime
        {
            public AgentBusImpl Bus { get; } = new();
            public HistoryManager History { get; } = new();
            public ContextDiffTracker Diffs { get; } = new();
            public ContextKeyRegistryImpl Keys { get; }
            public RecordingLog Log { get; } = new();
            public ContextOrchestrator Engine { get; }

            public ContextRuntime()
            {
                var cache = new ProviderCache(Bus, Log);
                Keys = new ContextKeyRegistryImpl(Log, cache);
                Engine = new ContextOrchestrator(History, (INpcManager?)null,
                    new ContextBuildServices(new ContextCacheManager(embedCache: new EmbedCache()), Diffs,
                        new ContextLayerBuilder(), new BudgetScheduler()),
                    null!, null!, new FlywheelParameterStore(), Log,
                    new EmbeddingSnapshotStore(), Keys, new RelevanceTableImpl(), cache);
            }

            public Task<ContextSnapshot?> Build()
                => Engine.BuildSnapshotFromEnvelopeAsync("pawn", "query", scenarioId: "context-test");
        }

        private sealed class RecordingLog : ILogSink
        {
            public List<string> Warnings { get; } = new();
            public void Message(string msg) { }
            public void Warning(string msg) => Warnings.Add(msg);
            public void Error(string msg) { }
            public void LogFromBackground(string msg, bool isWarning = false)
            {
                if (isWarning) Warnings.Add(msg);
            }
        }
    }
}
