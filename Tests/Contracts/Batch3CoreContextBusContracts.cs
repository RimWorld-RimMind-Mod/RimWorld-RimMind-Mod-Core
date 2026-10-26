using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Behaviours;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Application.Features.Storage;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Domain.Llm;
using RimMind.Domain.Settings;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Cache;
using RimMind.Presentation.Context;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class Batch3CoreContextBusContracts
    {
        [Fact]
        public async Task Context_caches_relevance_feedback_and_budget_scheduler_contracts()
        {
            await ContractCaseRunner.RunAsync(
                ("EmbedCache updates LRU on Get, removes empty NPC maps on invalidation, and protects L0/L1 layers", () =>
                {
                    var cache = new EmbedCache();
                    cache.SetBlockEmbedding("npc-protected", "rule", new[] { 1f }, ContextLayer.L0_Static);
                    cache.SetBlockEmbedding("npc-old", "state", new[] { 2f }, ContextLayer.L3_State);

                    for (int i = 0; i < 198; i++)
                    {
                        cache.SetBlockEmbedding($"npc-{i}", "k", new[] { (float)i }, ContextLayer.L3_State);
                    }

                    // Touch npc-old so it moves to the MRU tail; npc-0 should be evicted when capacity (200) is exceeded
                    Assert.NotNull(cache.GetBlockEmbedding("npc-old", "state"));
                    cache.SetBlockEmbedding("npc-new", "k", new[] { 99f }, ContextLayer.L3_State);

                    Assert.NotNull(cache.GetBlockEmbedding("npc-protected", "rule"));
                    Assert.NotNull(cache.GetBlockEmbedding("npc-old", "state"));
                    Assert.Null(cache.GetBlockEmbedding("npc-0", "k"));

                    cache.SetEntryEmbedding("npc-entry", "mem", 0, new[] { 3f });
                    cache.InvalidateEntries("npc-entry", "mem");
                    Assert.Null(cache.GetEntryEmbedding("npc-entry", "mem", 0));

                    return Task.CompletedTask;
                }),
                ("ProviderCache skips trace-fallback caching and ContextOrchestrator.InvalidateNpc evicts pawn entries", async () =>
                {
                    var runtime = new AsyncContextBuildContracts.ContextRuntime();
                    int pawnCalls = 0;
                    runtime.Keys.Register(new ContextProviderDef(
                        "pawn_state",
                        ContextLayer.L3_State,
                        1.0f,
                        (_, _) => Task.FromResult<string?>($"state-{++pawnCalls}"),
                        stalenessTicks: 6000,
                        cacheScope: CacheScope.Pawn));

                    await runtime.Build();
                    await runtime.Build();
                    Assert.Equal(1, pawnCalls);

                    runtime.Engine.InvalidateNpc("pawn");
                    await runtime.Build();
                    Assert.Equal(2, pawnCalls);

                    var standaloneCache = new ProviderCache();
                    int fallbackCalls = 0;
                    var pawnDef = new ContextProviderDef(
                        "trace_only",
                        ContextLayer.L3_State,
                        1.0f,
                        (_, _) => Task.FromResult<string?>($"v-{++fallbackCalls}"),
                        stalenessTicks: 6000,
                        cacheScope: CacheScope.Pawn);
                    var anonCtx = new ProviderContext("dialogue", "trace-1") { PawnId = 0, NpcId = null };
                    await standaloneCache.GetOrComputeAsync(pawnDef, anonCtx, CancellationToken.None);
                    await standaloneCache.GetOrComputeAsync(pawnDef, anonCtx, CancellationToken.None);
                    Assert.Equal(2, fallbackCalls);
                    Assert.Equal(0, standaloneCache.Count);
                }),
                ("ContextFeedbackMiddleware and AIResponseAnalyzer learn per-key usage instead of LayerTag names", async () =>
                {
                    var learner = new RelevanceLearner();
                    var analyzer = new AIResponseAnalyzer();
                    var mw = new ContextFeedbackMiddleware(learner, analyzer);

                    var snapshot = new ContextSnapshot
                    {
                        NpcId = "npc-1",
                        Scenario = "dialogue",
                        IncludedKeys = new[] { "mood", "weather" }
                    };
                    snapshot.AddMessage(new ChatMessage
                    {
                        Role = "system",
                        LayerTag = "L3",
                        Content = "<layer_L3>\n[mood] Critically starving and exhausted in the freezer\n[weather] Clear sunny sky 22C\n</layer_L3>"
                    });

                    var envelope = new LlmRequestEnvelope
                    {
                        RequestId = "req-1",
                        TraceId = "trace-feedback-1",
                        ScenarioId = "dialogue",
                        ModId = "Test"
                    };
                    var ctx = new LlmRequestContext(envelope) { Snapshot = snapshot };

                    await mw.InvokeAsync(ctx, c =>
                    {
                        c.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                        {
                            Content = "I am critically starving and exhausted right now."
                        });
                        return Task.CompletedTask;
                    });

                    Assert.Equal(1.0f, learner.GetFeedbackScore("dialogue", "mood"));
                    Assert.Equal(0.0f, learner.GetFeedbackScore("dialogue", "weather"));
                }),
                ("BudgetScheduler isolates pawn cooldowns, populates BudgetAllocation.KeyScores, and trims optional keys over budget", () =>
                {
                    var scheduler = new BudgetScheduler();
                    var l0Key = new KeyMeta("sys", ContextLayer.L0_Static, 1.0f, _ => new List<ContextEntry>(), "Core") { CurrentE = 50f };
                    var highOpt = new KeyMeta("high_opt", ContextLayer.L3_State, 0.9f, _ => new List<ContextEntry>(), "Core") { CurrentE = 100f };
                    var lowOpt = new KeyMeta("low_opt", ContextLayer.L3_State, 0.1f, _ => new List<ContextEntry>(), "Core") { CurrentE = 100f };
                    var keys = new List<KeyMeta> { l0Key, highOpt, lowOpt };

                    var pawnACtx = new ScoringContext { Scenario = "dialogue", NpcId = "pawn-A", NowTicks = 1000 };
                    var allocA = scheduler.ScheduleWithContext(keys, pawnACtx, budget: 180f);

                    // Shared KeyMeta instances must not be mutated
                    Assert.Equal(0, highOpt.LastIncludedTick);
                    Assert.Equal(0f, highOpt.CurrentScore);

                    // Per-call scores recorded in BudgetAllocation.KeyScores; lowOpt trimmed because 50 + 100 + 100 > 180
                    Assert.True(allocA.KeyScores["high_opt"] > allocA.KeyScores["low_opt"]);
                    Assert.Single(allocA.L0Keys);
                    Assert.Single(allocA.L3Keys);
                    Assert.Equal("high_opt", allocA.L3Keys[0].Key);

                    // Pawn B at tick 1100 does not suffer Pawn A's cooldown penalty on high_opt
                    var pawnBCtx = new ScoringContext { Scenario = "dialogue", NpcId = "pawn-B", NowTicks = 1100 };
                    var pawnAAgainCtx = new ScoringContext { Scenario = "dialogue", NpcId = "pawn-A", NowTicks = 1100 };
                    var allocB = scheduler.ScheduleWithContext(keys, pawnBCtx, budget: 500f);
                    var allocAAgain = scheduler.ScheduleWithContext(keys, pawnAAgainCtx, budget: 500f);
                    Assert.True(allocB.KeyScores["high_opt"] > allocAAgain.KeyScores["high_opt"]);

                    return Task.CompletedTask;
                }));
        }

        [Fact]
        public async Task Bus_pipeline_remote_sync_agent_decision_and_queue_edge_case_contracts()
        {
            await ContractCaseRunner.RunAsync(
                ("AgentBus logs subscriber exceptions to ILogSink and MutablePipeline reuses compiled delegate chain", async () =>
                {
                    var log = new TestLogSink();
                    var bus = new AgentBusImpl();
                    bus.SetLogSink(log);
                    Assert.Same(bus.DispatchAction, bus.DispatchAction);

                    bus.Subscribe<PerceptionEvent>(_ => throw new InvalidOperationException("boom-subscriber"));
                    bus.Publish(new PerceptionEvent("npc-1", 1, "sense", "data"));
                    Assert.Contains(log.Errors, e => e.Contains("boom-subscriber", StringComparison.Ordinal));

                    var pipeline = new MutablePipeline<BusPublishContext>();
                    var order = new List<int>();
                    pipeline.Use(new OrderMiddleware(20, order));
                    pipeline.Use(new OrderMiddleware(10, order));

                    await pipeline.ExecuteAsync(new BusPublishContext(new PerceptionEvent("npc-1", 1, "s", "d")));
                    await pipeline.ExecuteAsync(new BusPublishContext(new PerceptionEvent("npc-1", 1, "s", "d")));
                    Assert.Equal(new[] { 10, 20, 10, 20 }, order);
                }),
                ("RemoteSyncOrchestrator debounces pushes per key rather than globally", async () =>
                {
                    var backend = new RecordingRemoteBackend();
                    var orchestrator = new RemoteSyncOrchestrator(
                        backend,
                        new RemoteSyncSettings { AutoPush = true, PushDebounceSeconds = 60 });

                    var firstKey = RemoteKeys.PawnMemory(1);
                    var secondKey = RemoteKeys.PawnMemory(2);

                    var r1 = await orchestrator.EnqueuePushAsync(firstKey, "{\"a\":1}", 1, CancellationToken.None);
                    var r2 = await orchestrator.EnqueuePushAsync(secondKey, "{\"b\":2}", 1, CancellationToken.None);
                    var r1Duplicate = await orchestrator.EnqueuePushAsync(firstKey, "{\"a\":2}", 2, CancellationToken.None);

                    Assert.True(r1.IsOk && r1.Value);
                    Assert.True(r2.IsOk && r2.Value);
                    Assert.True(r1Duplicate.IsOk && !r1Duplicate.Value);
                    Assert.Equal(new[] { firstKey, secondKey }, backend.PushedKeys);
                }),
                ("DecisionProcessor publishes failure on invalid tool and ThinkStrategyHelper ignores prose parentheticals", () =>
                {
                    Assert.False(ThinkStrategyHelper.IsQueryToolName(null));
                    Assert.False(ThinkStrategyHelper.IsQueryToolName("   "));
                    Assert.True(ThinkStrategyHelper.IsQueryToolName("query_tool_help"));

                    var prose = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                    {
                        Content = "I'm tired (reason: no sleep)"
                    });
                    Assert.True(prose.IsOk);
                    Assert.Equal("dialogue.free", prose.Value.ActionIntent);

                    var validFallback = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                    {
                        Content = "place_blueprint(building_def=\"Bed\", reason=\"Need sleep\")"
                    });
                    Assert.True(validFallback.IsOk);
                    Assert.Equal("place_blueprint", validFallback.Value.ActionIntent);

                    Assert.Same(ThinkStrategyHelper.BuildDecisionExamples(), ThinkStrategyHelper.BuildDecisionExamples());

                    var bus = new AgentBusImpl();
                    DecisionFailedEvent? failedEvt = null;
                    bus.Subscribe<DecisionFailedEvent>(e => failedEvt = e);
                    var agent = new StubAgentControl();
                    var registry = new ToolRegistry();
                    var processor = new DecisionProcessor(
                        agent,
                        bus,
                        new StubTickProvider(42),
                        () => { },
                        () => { },
                        _ => { },
                        _ => Result<Unit, RimMindError>.Ok(Unit.Value),
                        () => 7,
                        toolRegistry: registry);

                    var result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                    {
                        Content = "<Action>{\"action\":\"nonexistent_hallucinated_tool\",\"reason\":\"test\"}</Action>"
                    });
                    bool done = processor.ProcessResult(
                        result,
                        null,
                        new ReactiveAgentMode().GetThinkStrategy(),
                        Array.Empty<ToolDefinition>(),
                        0);

                    Assert.True(done);
                    Assert.NotNull(failedEvt);
                    Assert.Equal("nonexistent_hallucinated_tool", failedEvt!.ActionIntent);
                    Assert.Single(agent.RecordedBehaviors);
                    Assert.False(agent.RecordedBehaviors[0].Success);

                    return Task.CompletedTask;
                }),
                ("RequestQueue clears active state before fence rejection, QueueCircuitBreaker avoids substring matches, and HistoryManager caps pending turns", async () =>
                {
                    var fence = new RejectingFence();
                    var queue = new RequestQueue(completionFence: fence);
                    var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    queue.Enqueue(
                        new LlmRequestEnvelope { RequestId = "fenced-req", ModId = "TestMod" },
                        _ => { },
                        _ =>
                        {
                            finished.TrySetResult(true);
                            return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "ok" }));
                        },
                        isLocalEndpoint: true);

                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    await Task.Delay(30);

                    Assert.Equal(0, queue.ActiveRequestCount);
                    Assert.False(queue.IsLocalModelBusy);
                    // Subsequent Tick with expired timeout must not throw ObjectDisposedException on disposed CTS
                    queue.CurrentTick = 10000;
                    queue.Tick();

                    var cooldownRegistry = new StubCooldownRegistry(new IModCooldown[]
                    {
                        new StubModCooldown("Mod", "Mod", 120),
                        new StubModCooldown("RimMind.Advisor", "RimMind.Advisor", 300)
                    });
                    var cb = new QueueCircuitBreaker(new DefaultSettingsProvider(), modCooldowns: cooldownRegistry);
                    Assert.Equal(0, cb.GetModCooldownTicks("OtherMod"));
                    Assert.Equal(300, cb.GetModCooldownTicks("RimMind.Advisor.Alert"));

                    var history = new HistoryManager();
                    for (int i = 0; i <= 110; i++)
                    {
                        history.AddPendingTurn("npc-pending", $"t-{i}", $"u-{i}", "...");
                    }
                    Assert.True(history.GetHistoryCount("npc-pending") <= 200);
                }));
        }

        private sealed class TestLogSink : ILogSink
        {
            public List<string> Errors { get; } = new();
            public void Message(string msg) { }
            public void Warning(string msg) { }
            public void Error(string msg) => Errors.Add(msg);
            public void LogFromBackground(string msg, bool isWarning = false) { }
        }

        private sealed class OrderMiddleware : IMiddleware<BusPublishContext>
        {
            private readonly List<int> _order;
            public OrderMiddleware(int order, List<int> log) { Order = order; _order = log; }
            public string Name => $"M{Order}";
            public int Order { get; }
            public string Id => Name;
            public string OwnerModId => "Test";
            public Task InvokeAsync(BusPublishContext context, MiddlewareDelegate<BusPublishContext> next)
            {
                _order.Add(Order);
                return next(context);
            }
        }

        private sealed class RecordingRemoteBackend : IRemoteBackend
        {
            public List<string> PushedKeys { get; } = new();
            public string ProviderName => "test";
            public bool IsConfigured => true;
            public Task<Result<RemoteEntry?, RimMindError>> PullAsync(string key, CancellationToken ct)
                => Task.FromResult(Result<RemoteEntry?, RimMindError>.Ok(null));
            public Task<Result<bool, RimMindError>> PushAsync(string key, string json, long version, CancellationToken ct)
            {
                PushedKeys.Add(key);
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }
            public Task<Result<string[], RimMindError>> ListKeysAsync(string prefix, CancellationToken ct)
                => Task.FromResult(Result<string[], RimMindError>.Ok(Array.Empty<string>()));
            public Task<Result<bool, RimMindError>> DeleteAsync(string key, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        private sealed class StubTickProvider : ITickProvider
        {
            public StubTickProvider(int ticks) => TicksGame = ticks;
            public int TicksGame { get; }
        }

        private sealed class StubAgentControl : IAgentControl
        {
            public List<BehaviorRecordDto> RecordedBehaviors { get; } = new();
            public bool IsActive => true;
            public AgentModeId CurrentModeId => AgentModeId.Reactive;
            public IAgentMode CurrentMode => null!;
            public bool IsPawnValid => true;
            public AgentState State => AgentState.Active;
            public string NpcId => "NPC-7";
            public string Label => "Seven";
            public int? LastThinkTick { get; set; }
            public int GoalCount => 0;
            public void Tick() { }
            public bool TransitionTo(AgentState newState) => true;
            public void ForceThink() { }
            public void SwitchMode(AgentModeId modeId) { }
            public void Cleanup() { }
            public void Destroy() { }
            public void ResubscribeEvents() { }
            public bool RemoveGoal(string goalDescription) => false;
            public void RecordBehavior(BehaviorRecordDto record) => RecordedBehaviors.Add(record);
            public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10) => RecordedBehaviors;
            public float GetRecentSuccessRate(int count = 10) => 0f;
            public string GetDebugInfo() => string.Empty;
            public object? ConsumePendingJob() => null;
        }

        private sealed class RejectingFence : ICompletionFence
        {
            public CancellationToken CancellationToken => CancellationToken.None;
            public bool TryAcceptCompletion() => false;
        }

        private sealed class StubModCooldown : IModCooldown
        {
            public StubModCooldown(string id, string ownerModId, int cooldownTicks)
            {
                Id = id;
                OwnerModId = ownerModId;
                CooldownTicks = cooldownTicks;
            }
            public string Id { get; }
            public string OwnerModId { get; }
            public int CooldownTicks { get; }
        }

        private sealed class StubCooldownRegistry : IExtensionRegistry<IModCooldown>
        {
            private readonly IReadOnlyList<IModCooldown> _items;
            public StubCooldownRegistry(IReadOnlyList<IModCooldown> items) => _items = items;
            public IReadOnlyList<IModCooldown> All => _items;
            public int Count => _items.Count;
            public void Register(IModCooldown extension) { }
            public bool Unregister(string id) => false;
            public int UnregisterByOwner(string ownerModId) => 0;
            public IModCooldown? FindById(string id) => _items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            public IReadOnlyList<IModCooldown> FindByOwner(string ownerModId) => _items.Where(x => x.OwnerModId == ownerModId).ToList();
            public void Clear() { }
        }
    }
}
