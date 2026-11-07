using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Behaviours;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Events;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P0CorePipelineBusContracts
    {
        [Fact]
        public async Task B1_AgentBus_SubscribeByName_rejects_unknown_fallback_and_binds_registered_event_types()
        {
            await ContractCaseRunner.RunAsync(
                ("unknown event name does not receive unrelated base AgentBusEvents and binds when registered later", () =>
                {
                    var bus = new AgentBusImpl();
                    int unknownCount = 0;
                    int customCount = 0;

                    bus.SubscribeByName("NeverRegisteredEvent", _ => unknownCount++);
                    bus.SubscribeByName(LateRegisteredCustomEvent.CustomName, _ => customCount++);

                    bus.Publish(new AgentBusEvent("NPC-1", 1, AgentBusEventType.WorkflowPhaseChange));
                    bus.Publish(new PerceptionEvent("NPC-1", 1, "test", "hello"));
                    Assert.Equal(0, unknownCount);
                    Assert.Equal(0, customCount);

                    AgentBusImpl.RegisterKnownEventType(LateRegisteredCustomEvent.CustomName, typeof(LateRegisteredCustomEvent));
                    bus.Publish(new LateRegisteredCustomEvent("NPC-1"));
                    Assert.Equal(0, unknownCount);
                    Assert.Equal(1, customCount);

                    return Task.CompletedTask;
                }),
                ("ProviderCache with custom invalidation trigger is only invalidated by its registered event", async () =>
                {
                    AgentBusImpl.RegisterKnownEventType(CustomCacheInvalidationEvent.CustomName, typeof(CustomCacheInvalidationEvent));
                    var runtime = new AsyncContextBuildContracts.ContextRuntime();
                    int computeCalls = 0;
                    runtime.Keys.Register(new ContextProviderDef(
                        "custom_cached_key",
                        ContextLayer.L3_State,
                        1.0f,
                        (_, _) => Task.FromResult<string?>($"val-{++computeCalls}"),
                        stalenessTicks: 6000,
                        invalidationTriggers: new[] { CustomCacheInvalidationEvent.CustomName },
                        cacheScope: CacheScope.Pawn));

                    await runtime.Build();
                    await runtime.Build();
                    Assert.Equal(1, computeCalls);

                    // Unrelated events must NOT invalidate custom_cached_key
                    runtime.Bus.Publish(new AgentBusEvent("pawn", 1, AgentBusEventType.WorkflowPhaseChange));
                    runtime.Bus.Publish(new PerceptionEvent("pawn", 1, "other", "msg"));
                    await runtime.Build();
                    Assert.Equal(1, computeCalls);

                    // Matching custom event invalidates the cached provider entry
                    runtime.Bus.Publish(new CustomCacheInvalidationEvent("pawn"));
                    var refreshed = await runtime.Build();
                    Assert.Equal(2, computeCalls);
                    Assert.Contains("val-2", refreshed!.Messages.Single(m => m.LayerTag == "L3").Content);
                }));
        }

        [Fact]
        public async Task B8_and_B16_Envelope_hints_populate_ProviderContext_and_BudgetScheduler_preserves_L4_History_keys()
        {
            await ContractCaseRunner.RunAsync(
                ("ContextBuildMiddleware threads LlmRequestEnvelope.Hints into ProviderContext.Hints and SpeakerName", async () =>
                {
                    var runtime = new AsyncContextBuildContracts.ContextRuntime();
                    string? observedChatHint = null;
                    string? observedMonologueHint = null;

                    runtime.Keys.Register(new ContextProviderDef(
                        "hint_probe",
                        ContextLayer.L0_Static,
                        1.0f,
                        (ctx, _) =>
                        {
                            observedChatHint = ctx.Hints?.TryGetValue("IsStorytellerChat", out var v1) == true ? v1?.ToString() : null;
                            observedMonologueHint = ctx.Hints?.TryGetValue("IsMonologue", out var v2) == true ? v2?.ToString() : null;
                            return Task.FromResult<string?>($"chat={observedChatHint};mono={observedMonologueHint}");
                        },
                        stalenessTicks: 0));

                    var envelope = LlmRequestEnvelopeBuilder
                        .ForNpc("pawn", new GameStateInfo().AddSection("dialogue_input", "Hello there"))
                        .ForScenarioId("context-test")
                        .WithHint("IsStorytellerChat", "true")
                        .WithHint("IsMonologue", "false")
                        .WithHint("SpeakerName", "Alice")
                        .Build();

                    var mw = new ContextBuildMiddleware(runtime.Engine);
                    var reqCtx = new LlmRequestContext(envelope);
                    await mw.InvokeAsync(reqCtx, _ => Task.CompletedTask);

                    Assert.Equal("true", observedChatHint);
                    Assert.Equal("false", observedMonologueHint);
                    Assert.NotNull(reqCtx.Snapshot);
                    Assert.Contains(reqCtx.Envelope.Messages, m => m.LayerTag == "L0" && m.Content.Contains("chat=true;mono=false"));
                    Assert.Contains(reqCtx.Envelope.Messages, m => m.Role == "user" && m.Content.Contains("Alice") && m.Content.Contains("Hello there"));
                }),
                ("BudgetScheduler and ContextOrchestrator preserve L4_History keys instead of dropping them", async () =>
                {
                    var runtime = new AsyncContextBuildContracts.ContextRuntime();
                    runtime.Keys.Register(new ContextProviderDef(
                        "l0_rule",
                        ContextLayer.L0_Static,
                        1.0f,
                        (_, _) => Task.FromResult<string?>("static-rule"),
                        stalenessTicks: 0));
                    runtime.Keys.Register(new ContextProviderDef(
                        "dialogue_history",
                        ContextLayer.L4_History,
                        0.8f,
                        (_, _) => Task.FromResult<string?>("recent-dialogue-turn"),
                        stalenessTicks: 0));

                    var snapshot = await runtime.Build();
                    Assert.NotNull(snapshot);
                    Assert.Contains("dialogue_history", snapshot!.IncludedKeys);
                    Assert.Contains(snapshot.AllEntries, e => e.SourceKey == "dialogue_history" && e.Content.Contains("recent-dialogue-turn"));
                    Assert.Contains(snapshot.Messages, m => m.LayerTag == "L4" && m.Content.Contains("recent-dialogue-turn"));
                }));
        }

        [Fact]
        public async Task B18_B19_and_B20_PerProvider_CircuitBreaker_MutablePipeline_recompilation_and_Envelope_cloning()
        {
            await ContractCaseRunner.RunAsync(
                ("CircuitBreakerMiddleware isolates circuit state per IAIClient so a tripped primary does not block failover", async () =>
                {
                    var settings = new StubBreakerSettings(threshold: 2, openDurationSec: 60);
                    var breaker = new CircuitBreakerMiddleware(settings);
                    var primaryClient = new StubClient("primary");
                    var failoverClient = new StubClient("failover");

                    // Trip primaryClient with 2 consecutive transient failures
                    for (int i = 0; i < 2; i++)
                    {
                        var ctx = new LlmRequestContext(new LlmRequestEnvelope { RequestId = $"p-{i}", ModId = "Test" })
                        {
                            Client = primaryClient
                        };
                        await breaker.InvokeAsync(ctx, c =>
                        {
                            c.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("primary down"));
                            return Task.CompletedTask;
                        });
                    }

                    Assert.Equal("Open", breaker.GetStateName(primaryClient));
                    Assert.Equal("Closed", breaker.GetStateName(failoverClient));

                    // Failover client must still pass through and succeed
                    var failoverCtx = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "f-1", ModId = "Test" })
                    {
                        Client = failoverClient
                    };
                    await breaker.InvokeAsync(failoverCtx, c =>
                    {
                        c.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "failover-ok" });
                        return Task.CompletedTask;
                    });
                    Assert.False(failoverCtx.IsShortCircuited);
                    Assert.True(failoverCtx.Result!.Value.IsOk);
                    Assert.Equal("failover-ok", failoverCtx.Result.Value.Value.Content);

                    // Primary client remains short-circuited
                    var primaryBlockedCtx = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "p-blocked", ModId = "Test" })
                    {
                        Client = primaryClient
                    };
                    await breaker.InvokeAsync(primaryBlockedCtx, _ => Task.CompletedTask);
                    Assert.True(primaryBlockedCtx.IsShortCircuited);
                    Assert.Equal("circuit_open", primaryBlockedCtx.ShortCircuitReason);
                }),
                ("MutablePipeline recompiles when extension middlewares are registered or unregistered after first ExecuteAsync", async () =>
                {
                    var registry = new StubMiddlewareExtensionRegistry();
                    var log = new List<string>();
                    var pipeline = new MutablePipeline<LlmRequestContext>();
                    pipeline.Use(new RecordingLlmMiddleware("base", 10, log));
                    pipeline.SetExtensionRegistry(registry);

                    // First execution before late extension registration
                    await pipeline.ExecuteAsync(new LlmRequestContext(new LlmRequestEnvelope { RequestId = "1" }));
                    Assert.Equal(new[] { "base" }, log);

                    // Late registration after first ExecuteAsync must recompile and execute in order
                    log.Clear();
                    registry.Register(new RecordingLlmMiddleware("late-ext", 5, log));
                    await pipeline.ExecuteAsync(new LlmRequestContext(new LlmRequestEnvelope { RequestId = "2" }));
                    Assert.Equal(new[] { "late-ext", "base" }, log);

                    // Unregistration must also recompile and remove the extension middleware
                    log.Clear();
                    Assert.True(registry.Unregister("late-ext"));
                    await pipeline.ExecuteAsync(new LlmRequestContext(new LlmRequestEnvelope { RequestId = "3" }));
                    Assert.Equal(new[] { "base" }, log);
                }),
                ("LlmRequestEnvelope.WithCancellationToken preserves all public instance properties via reflection", () =>
                {
                    using var cts1 = new CancellationTokenSource();
                    using var cts2 = new CancellationTokenSource();
                    Action<LlmChunk> streamCallback = _ => { };

                    var source = new LlmRequestEnvelope
                    {
                        RequestId = "req-123",
                        TraceId = "trace-456",
                        ScenarioId = "dialogue",
                        ModId = "RimMind.Dialogue",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } },
                        SystemAugmentations = new List<PromptAugmentation> { new PromptAugmentation("aug", "text", 10) },
                        JsonSchema = "{\"type\":\"object\"}",
                        Tools = new List<StructuredTool> { new StructuredTool { Name = "tool1", Description = "desc", Parameters = "{}" } },
                        ToolDispatchMode = ToolCallDispatchMode.Manual,
                        Examples = new List<ChatMessage> { new ChatMessage { Role = "assistant", Content = "ex" } },
                        MaxTokens = 1234,
                        Temperature = 0.42f,
                        Priority = AIRequestPriority.High,
                        ExpireAtTicks = 9999,
                        MaxRetryCount = 4,
                        IsStreaming = true,
                        OnStreamChunk = streamCallback,
                        Ct = cts1.Token,
                        NpcId = "NPC-42",
                        GameStateInfo = new GameStateInfo().AddSection("sec", "val"),
                        Hints = new Dictionary<string, string> { ["IsMonologue"] = "true" },
                    };

                    var cloned = source.WithCancellationToken(cts2.Token);
                    var props = typeof(LlmRequestEnvelope).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    Assert.NotEmpty(props);

                    foreach (var prop in props)
                    {
                        var sourceVal = prop.GetValue(source);
                        var clonedVal = prop.GetValue(cloned);
                        Assert.NotNull(sourceVal);

                        if (prop.Name == nameof(LlmRequestEnvelope.Ct))
                        {
                            Assert.Equal(cts2.Token, (CancellationToken)clonedVal!);
                            Assert.NotEqual(sourceVal, clonedVal);
                        }
                        else
                        {
                            Assert.Equal(sourceVal, clonedVal);
                        }
                    }

                    return Task.CompletedTask;
                }));
        }

        private sealed class LateRegisteredCustomEvent : AgentBusEvent
        {
            public const string CustomName = "LateRegisteredCustomEvent";
            public LateRegisteredCustomEvent(string? npcId)
            {
                NpcId = npcId ?? string.Empty;
            }
        }

        private sealed class CustomCacheInvalidationEvent : AgentBusEvent
        {
            public const string CustomName = "CustomCacheInvalidationEvent";
            public CustomCacheInvalidationEvent(string? npcId)
            {
                NpcId = npcId ?? string.Empty;
            }
        }

        private sealed class StubBreakerSettings : ICircuitBreakerSettings
        {
            public StubBreakerSettings(int threshold, int openDurationSec)
            {
                CircuitBreakerFailureThreshold = threshold;
                CircuitBreakerOpenDurationSec = openDurationSec;
            }
            public int CircuitBreakerFailureThreshold { get; }
            public int CircuitBreakerOpenDurationSec { get; }
        }

        private sealed class StubClient : IAIClient
        {
            private readonly string _id;
            public StubClient(string id) => _id = id;
            public bool IsConfigured() => true;
            public bool IsLocalEndpoint => false;
            public bool SupportsStreaming => false;
            public bool SupportsNpcServerState => false;
            public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
                => Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = _id }));
            public Task<Result<LlmResponse, RimMindError>> SendStreamAsync(LlmRequestEnvelope envelope, Action<LlmChunk> onChunk, CancellationToken ct = default)
                => SendAsync(envelope);
            public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
                => Task.FromResult(Result<List<string>, RimMindError>.Ok(new List<string>()));
            public void Dispose() { }
        }

        private sealed class StubMiddlewareExtensionRegistry : IExtensionRegistry<IMiddleware<LlmRequestContext>>
        {
            private readonly List<IMiddleware<LlmRequestContext>> _items = new List<IMiddleware<LlmRequestContext>>();
            public IReadOnlyList<IMiddleware<LlmRequestContext>> All => _items.ToList().AsReadOnly();
            public int Count => _items.Count;
            public void Register(IMiddleware<LlmRequestContext> extension)
            {
                _items.RemoveAll(x => string.Equals(x.Id, extension.Id, StringComparison.OrdinalIgnoreCase));
                _items.Add(extension);
            }
            public bool Unregister(string id) => _items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            public int UnregisterByOwner(string ownerModId) => _items.RemoveAll(x => x.OwnerModId == ownerModId);
            public IMiddleware<LlmRequestContext>? FindById(string id) => _items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            public IReadOnlyList<IMiddleware<LlmRequestContext>> FindByOwner(string ownerModId) => _items.Where(x => x.OwnerModId == ownerModId).ToList();
            public void Clear() => _items.Clear();
        }

        private sealed class RecordingLlmMiddleware : IMiddleware<LlmRequestContext>
        {
            private readonly List<string> _log;
            public RecordingLlmMiddleware(string id, int order, List<string> log)
            {
                Id = id;
                Name = id;
                Order = order;
                _log = log;
            }
            public string Id { get; }
            public string Name { get; }
            public int Order { get; }
            public string OwnerModId => "TestMod";
            public Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
            {
                _log.Add(Id);
                return next(context);
            }
        }
    }
}
