using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Registry;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P4CompositionAndLifecycleContracts
    {
        [Fact]
        public void GenerationCachedServiceRef_and_hub_refs_cache_across_generations_and_retirement()
        {
            ContractCaseRunner.Run(
                ("GenerationCachedServiceRef caches within a generation and refreshes only on newer generations", () =>
                {
                    var cache = new GenerationCachedServiceRef<ITestService>(required: true, scopeLabel: "runtime");
                    int resolverCalls = 0;

                    var first = cache.GetRequiredValue<RuntimeLifecycleState>(
                        () => cache.Refresh(1, RuntimeLifecycleState.Running, () =>
                        {
                            resolverCalls++;
                            return new TestService(10);
                        }),
                        static (type, state, gen) => new RuntimeServiceUnavailableException(type, state, gen));

                    var second = cache.GetRequiredValue<RuntimeLifecycleState>(
                        () => cache.Refresh(1, RuntimeLifecycleState.Running, () =>
                        {
                            resolverCalls++;
                            return new TestService(99);
                        }),
                        static (type, state, gen) => new RuntimeServiceUnavailableException(type, state, gen));

                    Assert.Equal(10, first.Version);
                    Assert.Same(first, second);
                    Assert.Equal(1, resolverCalls);
                    Assert.Equal(1, cache.BoundGeneration);

                    var updated = cache.GetRequiredValue<RuntimeLifecycleState>(
                        () => cache.Refresh(2, RuntimeLifecycleState.Running, () =>
                        {
                            resolverCalls++;
                            return new TestService(20);
                        }),
                        static (type, state, gen) => new RuntimeServiceUnavailableException(type, state, gen));

                    Assert.Equal(20, updated.Version);
                    Assert.Equal(2, resolverCalls);
                    Assert.Equal(2, cache.BoundGeneration);
                }),
                ("RuntimeServiceRef and GameServiceRef delegate to GenerationCachedServiceRef across publish and stop", () =>
                {
                    var runtimeHub = new RuntimeServiceHub(_ => { });
                    var runtimeBuilder1 = new RuntimeServiceBuilder();
                    runtimeBuilder1.Bind<ITestService>(new TestService(1));
                    var snapshot1 = runtimeBuilder1.Build();
                    runtimeHub.Publish(snapshot1, new RuntimeLifetime(snapshot1.RuntimeId, runtimeHub.IsCurrent, runtimeHub.RecordStaleCompletion));

                    var runtimeRequired = new RuntimeServiceRef<ITestService>(runtimeHub, required: true);
                    var runtimeOptional = new RuntimeServiceRef<ITestService>(runtimeHub, required: false);
                    Assert.Equal(1, runtimeRequired.Value.Version);
                    Assert.Equal(1, runtimeOptional.ValueOrDefault!.Version);
                    Assert.Equal(1, runtimeRequired.BoundGeneration);

                    var runtimeBuilder2 = new RuntimeServiceBuilder();
                    runtimeBuilder2.Bind<ITestService>(new TestService(2));
                    var snapshot2 = runtimeBuilder2.Build();
                    runtimeHub.Publish(snapshot2, new RuntimeLifetime(snapshot2.RuntimeId, runtimeHub.IsCurrent, runtimeHub.RecordStaleCompletion));

                    Assert.Equal(2, runtimeRequired.Value.Version);
                    Assert.Equal(2, runtimeOptional.ValueOrDefault!.Version);
                    Assert.Equal(2, runtimeRequired.BoundGeneration);

                    runtimeHub.Stop();
                    Assert.Null(runtimeOptional.ValueOrDefault);
                    Assert.Equal(3, runtimeOptional.BoundGeneration);
                    Assert.Throws<RuntimeServiceUnavailableException>(() => _ = runtimeRequired.Value);

                    var gameHub = new GameServiceHub(_ => { });
                    gameHub.Publish(new GameServiceBuilder().Bind<ITestService>(new TestService(7)).Build());
                    var gameRequired = new GameServiceRef<ITestService>(gameHub, required: true);
                    var gameOptional = new GameServiceRef<ITestService>(gameHub, required: false);

                    Assert.Equal(7, gameRequired.Value.Version);
                    Assert.Equal(7, gameOptional.ValueOrDefault!.Version);
                    Assert.Equal(1, gameRequired.BoundGeneration);

                    gameHub.Stop();
                    Assert.Null(gameOptional.ValueOrDefault);
                    Assert.Equal(2, gameOptional.BoundGeneration);
                    Assert.Throws<GameServiceUnavailableException>(() => _ = gameRequired.Value);
                }),
                ("RuntimeServiceSnapshot is the single source of truth for CompositionResult and RimMindRuntime", () =>
                {
                    string compositionRootSource = ReadSource("Presentation/Runtime/RimMindCompositionRoot.cs");
                    string runtimeSource = ReadSource("Presentation/Runtime/RimMindRuntime.cs");

                    Assert.Contains("new CompositionResult(services.Build())", compositionRootSource, StringComparison.Ordinal);
                    Assert.Contains("Services.GetRequired<", compositionRootSource, StringComparison.Ordinal);
                    Assert.Contains("_services.GetRequired<", runtimeSource, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void IGameScopedService_extensions_receive_start_and_stop_with_exception_isolation()
        {
            ContractCaseRunner.Run(
                ("all registered IGameScopedService extensions run on start and stop even when one throws", () =>
                {
                    var registry = new ExtensionRegistry<IGameScopedService>();
                    var events = new List<string>();
                    registry.Register(new RecordingGameScopedService("first", events, throwOnStart: true, throwOnStop: true));
                    registry.Register(new RecordingGameScopedService("second", events, throwOnStart: false, throwOnStop: false));
                    registry.Register(new RecordingGameScopedService("third", events, throwOnStart: true, throwOnStop: true));

                    RimMindRuntimeGameComponent.NotifyGameStarted(registry);
                    Assert.Equal(3, events.Count);
                    Assert.Contains("first:start", events);
                    Assert.Contains("second:start", events);
                    Assert.Contains("third:start", events);

                    events.Clear();
                    RimMindRuntimeGameComponent.NotifyGameStopped(registry);
                    Assert.Equal(3, events.Count);
                    Assert.Contains("first:stop", events);
                    Assert.Contains("second:stop", events);
                    Assert.Contains("third:stop", events);
                }),
                ("Player2GameLifecycleService is registered as an IGameScopedService and StopGameServices routes through NotifyGameStopped", () =>
                {
                    string gameCompSource = ReadSource("Presentation/Runtime/RimMindRuntimeGameComponent.cs");
                    string clientCompSource = ReadSource("Presentation/Runtime/Composition/ClientComposition.cs");

                    Assert.Contains("class Player2GameLifecycleService : IGameScopedService", gameCompSource, StringComparison.Ordinal);
                    Assert.Contains("NotifyGameStopped();", gameCompSource, StringComparison.Ordinal);
                    Assert.Contains("extensions.GetExtensionRegistry<IGameScopedService>()", clientCompSource, StringComparison.Ordinal);
                    Assert.Contains("Player2GameLifecycleService.Instance", clientCompSource, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void CompPawnAgent_consolidates_TearDownAgent_and_serializes_AutonomyDomain_dynamically()
        {
            ContractCaseRunner.Run(
                ("CompPawnAgent uses TearDownAgent across non-colonist tick, kill, destroy, and invalid/stale agent cleanup", () =>
                {
                    string compSource = ReadSource("Infrastructure/Verse/CompPawnAgent.cs");
                    Assert.Contains("private void TearDownAgent()", compSource, StringComparison.Ordinal);
                    Assert.True(Regex.Matches(compSource, @"\bTearDownAgent\s*\(\s*\)").Count >= 6);

                    var pawn = new Pawn { thingIDNumber = 91, Spawned = true, IsColonist = true, Map = new Map() };
                    var comp = new CompPawnAgent { parent = pawn };
                    pawn.AllComps.Add(comp);
                    var agent = new SpyPawnAgent(pawn);
                    comp.Agent = agent;

                    comp.Notify_Killed(pawn.Map!);
                    Assert.Null(comp.Agent);
                    Assert.Equal(1, agent.DestroyCount);

                    var secondAgent = new SpyPawnAgent(pawn);
                    comp.Agent = secondAgent;
                    comp.PostDestroy(DestroyMode.Vanish, pawn.Map!);
                    Assert.Null(comp.Agent);
                    Assert.Equal(1, secondAgent.DestroyCount);
                }),
                ("CompPawnAgent.PostExposeData iterates Enum.GetValues(typeof(AutonomyDomain)) using autonomy_ prefix", () =>
                {
                    string compSource = ReadSource("Infrastructure/Verse/CompPawnAgent.cs");
                    Assert.Contains("(AutonomyDomain[])Enum.GetValues(typeof(AutonomyDomain))", compSource, StringComparison.Ordinal);
                    Assert.Contains("\"autonomy_\" + domain.ToString()", compSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("autonomySurvival", compSource, StringComparison.Ordinal);

                    var pawn = new Pawn { thingIDNumber = 92, Spawned = true, IsColonist = true, Map = new Map() };
                    var comp = new CompPawnAgent { parent = pawn };
                    comp.AutonomyPolicy.SetLevel(AutonomyDomain.BlueprintPlacement, AutonomyLevel.FullAutonomous);

                    var previousMode = Scribe.mode;
                    try
                    {
                        Scribe.mode = LoadSaveMode.Saving;
                        comp.PostExposeData();
                        Assert.Equal(AutonomyLevel.FullAutonomous, comp.AutonomyPolicy.GetLevel(AutonomyDomain.BlueprintPlacement));
                    }
                    finally
                    {
                        Scribe.mode = previousMode;
                    }
                }));
        }

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
            {
                directory = directory.Parent;
            }

            return File.ReadAllText(Path.Combine(
                directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
                "RimMind-Core",
                "Source",
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private interface ITestService
        {
            int Version { get; }
        }

        private sealed class TestService : ITestService
        {
            public TestService(int version) => Version = version;
            public int Version { get; }
        }

        private sealed class RecordingGameScopedService : IGameScopedService
        {
            private readonly List<string> _events;
            private readonly bool _throwOnStart;
            private readonly bool _throwOnStop;

            public RecordingGameScopedService(string id, List<string> events, bool throwOnStart, bool throwOnStop)
            {
                Id = id;
                _events = events;
                _throwOnStart = throwOnStart;
                _throwOnStop = throwOnStop;
            }

            public string Id { get; }
            public string OwnerModId => "test.mod";

            public void OnGameStarted()
            {
                _events.Add($"{Id}:start");
                if (_throwOnStart)
                    throw new InvalidOperationException($"{Id} start failure");
            }

            public void OnGameStopped()
            {
                _events.Add($"{Id}:stop");
                if (_throwOnStop)
                    throw new InvalidOperationException($"{Id} stop failure");
            }
        }

        private sealed class SpyPawnAgent : IPawnAgentVerse
        {
            public SpyPawnAgent(Pawn pawn) => Pawn = pawn;
            public int DestroyCount { get; private set; }
            public Pawn Pawn { get; }
            public AgentState State { get; private set; } = AgentState.Active;
            public bool IsActive => State == AgentState.Active;
            public bool IsPawnValid => true;
            public string NpcId => "Pawn_" + Pawn.thingIDNumber;
            public string Label => Pawn.LabelShort;
            public int? LastThinkTick { get; set; }
            public int GoalCount => 0;
            public AgentAutonomyLevel AutonomyLevel { get; set; }
            public AgentWorkflowPhase WorkflowPhase { get; private set; } = AgentWorkflowPhase.Idle;
            public AgentModeId CurrentModeId => AgentModeId.Reactive;
            public IAgentMode CurrentMode => null!;
            public AgentIdentity Identity { get; set; } = new AgentIdentity("Pawn_test", 1, "TestPawn");
            public AgentGoalStack GoalStack { get; } = new AgentGoalStack();
            public IReadOnlyList<BehaviorRecord> BehaviorHistory => Array.Empty<BehaviorRecord>();
            public IStrategyOptimizer StrategyOptimizer => null!;
            public IPerceptionBuffer PerceptionBuffer => null!;
            public AgentIntentSession? ActiveIntentSession { get; private set; }

            public bool TransitionTo(AgentState newState)
            {
                State = newState;
                return true;
            }

            public void SwitchMode(AgentModeId modeId) { }
            public void Tick() { }
            public void ForceThink() { }
            public void Cleanup() { }
            public void ResubscribeEvents() { }
            public void Destroy()
            {
                DestroyCount++;
                State = AgentState.Terminated;
            }
            public string GetDebugInfo() => string.Empty;
            public IReadOnlyList<BehaviorRecord> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecord>();
            IReadOnlyList<BehaviorRecordDto> IAgentInfo.GetRecentHistory(int count) => Array.Empty<BehaviorRecordDto>();
            public float GetRecentSuccessRate(int count = 10) => 0f;
            public void AddGoal(AgentGoal goal) { }
            public bool RemoveGoal(string goalDescription) => false;
            public void RecordBehavior(BehaviorRecordDto record) { }
            public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision) => default;
            public void TransitionWorkflow(AgentWorkflowPhase target) => WorkflowPhase = target;
            public global::Verse.AI.Job? ConsumePendingJob() => null;
            object? IJobProvider.ConsumePendingJob() => null;
            public void SetPendingJob(global::Verse.AI.Job job) { }
            public void StartIntentSession(AgentIntentSession session) => ActiveIntentSession = session;
            public void ClearIntentSession() => ActiveIntentSession = null;
        }
    }
}
