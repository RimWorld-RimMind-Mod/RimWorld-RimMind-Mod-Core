using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Threading;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    [Collection("MechanismToolHandler")]
    public sealed class ToolCallDispatchMiddlewareContract
    {
        [Fact]
        public async Task Throwing_tool_is_isolated_and_remaining_calls_still_run_once()
        {
            var thrower = new CountingHandler("tool_a", throwOnExecute: true);
            var ok = new CountingHandler("tool_b", throwOnExecute: false);
            var middleware = new ToolCallDispatchMiddleware(new FakeRegistry(thrower, ok));
            var context = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "req-1", ModId = "TestMod" });

            await middleware.InvokeAsync(context, ctx =>
            {
                ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                {
                    ToolCallsJson = "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"tool_a\",\"arguments\":\"{}\"}}," +
                                    "{\"id\":\"c2\",\"type\":\"function\",\"function\":{\"name\":\"tool_b\",\"arguments\":\"{}\"}}]"
                });
                return Task.CompletedTask;
            });

            Assert.NotNull(context.ToolCallResults);
            Assert.Equal(2, context.ToolCallResults!.Count);
            Assert.True(context.ToolCallResults[0].IsError);
            Assert.False(context.ToolCallResults[1].IsError);
            Assert.Equal(1, thrower.Calls);
            Assert.Equal(1, ok.Calls);
            Assert.True(context.Result!.Value.IsOk); // a tool bug must not turn into an LLM failure
        }

        [Fact]
        public void Dispatch_runs_outside_retry_and_circuit_breaker_scopes()
        {
            // Lower order = outer. Dispatch must wrap Retry/CircuitBreaker/Telemetry so tool side effects
            // execute once per final LLM response instead of once per retry attempt.
            Assert.True(RimMindDefaults.MiddlewareOrder.ToolCallDispatch < RimMindDefaults.MiddlewareOrder.Telemetry);
            Assert.True(RimMindDefaults.MiddlewareOrder.ToolCallDispatch < RimMindDefaults.MiddlewareOrder.CircuitBreaker);
            Assert.True(RimMindDefaults.MiddlewareOrder.ToolCallDispatch < RimMindDefaults.MiddlewareOrder.UnifiedRetry);
        }

        [Fact]
        public async Task Decision_scenarios_defer_mutation_tools_and_mechanism_writes_reject_off_main_thread()
        {
            await ContractCaseRunner.RunAsync(
                ("Decision, AgentDecision, and AdvisorDecision defer mutation tools while allowing query tools", async () =>
                {
                    foreach (string scenarioId in new[] { ScenarioIds.Decision, ScenarioIds.AgentDecision, ScenarioIds.AdvisorDecision })
                    {
                        Assert.True(ScenarioIds.IsDecisionScenario(scenarioId));

                        var mutation = new CountingHandler("pawn.job.set", throwOnExecute: false, ToolKind.Mutation);
                        var compositeMutation = new CountingHandler("actions_stabilize_rest", throwOnExecute: false, ToolKind.Mutation);
                        var query = new CountingHandler("pawn.health.query", throwOnExecute: false, ToolKind.Query);
                        var middleware = new ToolCallDispatchMiddleware(new FakeRegistry(mutation, compositeMutation, query));

                        var context = new LlmRequestContext(new LlmRequestEnvelope
                        {
                            RequestId = $"req-{scenarioId}",
                            ModId = "RimMind.Agent",
                            ScenarioId = scenarioId,
                            ToolDispatchMode = ToolCallDispatchMode.Auto
                        });

                        await middleware.InvokeAsync(context, ctx =>
                        {
                            ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                            {
                                ToolCallsJson =
                                    "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"pawn.job.set\",\"arguments\":\"{}\"}}," +
                                    "{\"id\":\"c2\",\"type\":\"function\",\"function\":{\"name\":\"actions_stabilize_rest\",\"arguments\":\"{}\"}}," +
                                    "{\"id\":\"c3\",\"type\":\"function\",\"function\":{\"name\":\"pawn.health.query\",\"arguments\":\"{}\"}}]"
                            });
                            return Task.CompletedTask;
                        });

                        Assert.Equal(0, mutation.Calls);
                        Assert.Equal(0, compositeMutation.Calls);
                        Assert.Equal(1, query.Calls);
                        Assert.NotNull(context.ToolCallResults);
                        Assert.Single(context.ToolCallResults!);
                        Assert.Equal("pawn.health.query", context.ToolCallResults![0].ToolName);
                    }
                }),
                ("Decision scenarios with only mutation tools leave ToolCallResults null and never invoke handlers", async () =>
                {
                    foreach (string scenarioId in new[] { ScenarioIds.Decision, ScenarioIds.AgentDecision, ScenarioIds.AdvisorDecision })
                    {
                        var mutation = new CountingHandler("pawn.draft.toggle", throwOnExecute: false, ToolKind.Mutation);
                        var middleware = new ToolCallDispatchMiddleware(new FakeRegistry(mutation));
                        var context = new LlmRequestContext(new LlmRequestEnvelope
                        {
                            RequestId = $"req-mut-{scenarioId}",
                            ModId = "RimMind.Agent",
                            ScenarioId = scenarioId,
                            ToolDispatchMode = ToolCallDispatchMode.Auto
                        });

                        await middleware.InvokeAsync(context, ctx =>
                        {
                            ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                            {
                                ToolCallsJson = "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"pawn.draft.toggle\",\"arguments\":\"{}\"}}]"
                            });
                            return Task.CompletedTask;
                        });

                        Assert.Equal(0, mutation.Calls);
                        Assert.Null(context.ToolCallResults);
                    }
                }),
                ("MechanismToolHandler fails fast on write operations invoked off the Unity main thread", async () =>
                {
                    var prev = MechanismToolHandler.IsMainThreadPredicate;
                    try
                    {
                        MechanismToolHandler.IsMainThreadPredicate = () => false;
                        var fakeMech = new CountingMechanism();
                        var setHandler = new MechanismToolHandler(fakeMech, fakeMech, fakeMech, fakeMech, MechanismOperationType.Set);
                        var result = await setHandler.ExecuteAsync(new ToolCallArgs
                        {
                            ToolCallId = "tc-offthread",
                            ToolName = setHandler.Id,
                            ArgumentsJson = "{\"pawn_id\":12}"
                        }, CancellationToken.None);

                        Assert.True(result.IsOk);
                        Assert.True(result.Value.IsError);
                        Assert.Contains("main thread", result.Value.Content, StringComparison.OrdinalIgnoreCase);
                        Assert.Equal(0, fakeMech.WriteCalls);
                    }
                    finally
                    {
                        MechanismToolHandler.IsMainThreadPredicate = prev;
                    }
                }));
        }

        [Fact]
        public async Task Main_thread_marshaling_executes_off_thread_calls_on_main_thread_handles_timeout_and_isolates_throws()
        {
            await ContractCaseRunner.RunAsync(
                ("off-main-thread query handler is marshaled to main thread and sees IsMainThread == true", async () =>
                {
                    using var onMainThread = new ThreadLocal<bool>(() => false);
                    bool? observedMainThread = null;
                    var reader = new MainThreadReader(
                        post: action => Task.Run(() =>
                        {
                            onMainThread.Value = true;
                            try { action(); }
                            finally { onMainThread.Value = false; }
                        }),
                        isMainThread: () => onMainThread.Value);

                    var queryHandler = new CallbackHandler(
                        "pawn.health.query",
                        ToolKind.Query,
                        (args, _) =>
                        {
                            observedMainThread = onMainThread.Value;
                            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("healthy", args.ToolCallId, "pawn.health.query")));
                        });

                    var middleware = new ToolCallDispatchMiddleware(
                        new FakeRegistry(queryHandler),
                        mainThreadReader: reader);

                    var context = new LlmRequestContext(new LlmRequestEnvelope
                    {
                        RequestId = "req-mt-ok",
                        ModId = "RimMind.Agent",
                        ScenarioId = ScenarioIds.AgentDecision,
                        ToolDispatchMode = ToolCallDispatchMode.Auto
                    });

                    Assert.False(reader.IsMainThread);
                    await middleware.InvokeAsync(context, ctx =>
                    {
                        ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                        {
                            ToolCallsJson = "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"pawn.health.query\",\"arguments\":\"{}\"}}]"
                        });
                        return Task.CompletedTask;
                    });

                    Assert.Equal(true, observedMainThread);
                    Assert.NotNull(context.ToolCallResults);
                    Assert.Single(context.ToolCallResults!);
                    Assert.False(context.ToolCallResults![0].IsError);
                    Assert.Equal("healthy", context.ToolCallResults![0].Content);
                }),
                ("main-thread dispatch timeout returns IsError ToolResult cleanly without throwing", async () =>
                {
                    int handlerCalls = 0;
                    var stalledReader = new MainThreadReader(
                        post: _ => { /* intentionally never drained */ },
                        isMainThread: () => false);

                    var queryHandler = new CallbackHandler(
                        "pawn.health.query",
                        ToolKind.Query,
                        (args, _) =>
                        {
                            Interlocked.Increment(ref handlerCalls);
                            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("healthy", args.ToolCallId, "pawn.health.query")));
                        });

                    var middleware = new ToolCallDispatchMiddleware(
                        new FakeRegistry(queryHandler),
                        mainThreadReader: stalledReader,
                        mainThreadTimeout: TimeSpan.FromMilliseconds(40));

                    var context = new LlmRequestContext(new LlmRequestEnvelope
                    {
                        RequestId = "req-mt-timeout",
                        ModId = "RimMind.Agent",
                        ScenarioId = ScenarioIds.AgentDecision,
                        ToolDispatchMode = ToolCallDispatchMode.Auto
                    });

                    await middleware.InvokeAsync(context, ctx =>
                    {
                        ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                        {
                            ToolCallsJson = "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"pawn.health.query\",\"arguments\":\"{}\"}}]"
                        });
                        return Task.CompletedTask;
                    });

                    Assert.Equal(0, handlerCalls);
                    Assert.NotNull(context.ToolCallResults);
                    Assert.Single(context.ToolCallResults!);
                    Assert.True(context.ToolCallResults![0].IsError);
                    Assert.Contains("timed out waiting for Unity main thread", context.ToolCallResults![0].Content, StringComparison.OrdinalIgnoreCase);
                    Assert.True(context.Result!.Value.IsOk);
                }),
                ("throwing handler marshaled to main thread is isolated and remaining calls still execute", async () =>
                {
                    using var onMainThread = new ThreadLocal<bool>(() => false);
                    bool? okObservedMainThread = null;
                    var reader = new MainThreadReader(
                        post: action => Task.Run(() =>
                        {
                            onMainThread.Value = true;
                            try { action(); }
                            finally { onMainThread.Value = false; }
                        }),
                        isMainThread: () => onMainThread.Value);

                    var thrower = new CallbackHandler(
                        "pawn.broken.query",
                        ToolKind.Query,
                        (_, _) => throw new InvalidOperationException("main-thread boom"));
                    var ok = new CallbackHandler(
                        "pawn.ok.query",
                        ToolKind.Query,
                        (args, _) =>
                        {
                            okObservedMainThread = onMainThread.Value;
                            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("survived", args.ToolCallId, "pawn.ok.query")));
                        });

                    var middleware = new ToolCallDispatchMiddleware(
                        new FakeRegistry(thrower, ok),
                        mainThreadReader: reader);

                    var context = new LlmRequestContext(new LlmRequestEnvelope
                    {
                        RequestId = "req-mt-throw",
                        ModId = "RimMind.Agent",
                        ScenarioId = ScenarioIds.AgentDecision,
                        ToolDispatchMode = ToolCallDispatchMode.Auto
                    });

                    await middleware.InvokeAsync(context, ctx =>
                    {
                        ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                        {
                            ToolCallsJson =
                                "[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"pawn.broken.query\",\"arguments\":\"{}\"}}," +
                                "{\"id\":\"c2\",\"type\":\"function\",\"function\":{\"name\":\"pawn.ok.query\",\"arguments\":\"{}\"}}]"
                        });
                        return Task.CompletedTask;
                    });

                    Assert.Equal(true, okObservedMainThread);
                    Assert.NotNull(context.ToolCallResults);
                    Assert.Equal(2, context.ToolCallResults!.Count);
                    Assert.True(context.ToolCallResults![0].IsError);
                    Assert.Contains("main-thread boom", context.ToolCallResults![0].Content, StringComparison.OrdinalIgnoreCase);
                    Assert.False(context.ToolCallResults![1].IsError);
                    Assert.Equal("survived", context.ToolCallResults![1].Content);
                    Assert.True(context.Result!.Value.IsOk);
                }));
        }

        private sealed class CallbackHandler : IToolHandler
        {
            private readonly ToolKind _kind;
            private readonly Func<ToolCallArgs, CancellationToken, Task<Result<ToolResult, RimMindError>>> _execute;

            public CallbackHandler(
                string id,
                ToolKind kind,
                Func<ToolCallArgs, CancellationToken, Task<Result<ToolResult, RimMindError>>> execute)
            {
                Id = id;
                _kind = kind;
                _execute = execute;
            }

            public string Id { get; }
            public string OwnerModId => "TestMod";
            public ToolDefinition Definition => new ToolDefinition
            {
                Id = Id,
                Manifest = new ToolManifest { Kind = _kind }
            };

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
                => _execute(args, ct);
        }

        private sealed class CountingMechanism : IMechanismReader, IMechanismWriter, IMechanismTrigger, IMechanismMetadata
        {
            public int WriteCalls { get; private set; }
            public string Id => MechanismId;
            public string OwnerModId => "TestMod";
            public string MechanismId => "pawn.job";
            public MechanismScope Scope => MechanismScope.Pawn;
            public MechanismRisk Risk => MechanismRisk.Moderate;
            public IReadOnlyList<MechanismOperationType> SupportedOperations => new[] { MechanismOperationType.Set };
            public MechanismDocs Docs => new MechanismDocs { Summary = "test" };
            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => null;
            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;
            public Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
                => Task.FromResult(Result<string, RimMindError>.Ok("{}"));
            public Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
                => Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(Array.Empty<MechanismEnumResult>()));
            public Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
            public Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
            public Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
            public Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
            public Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
            public Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct)
            { WriteCalls++; return Task.FromResult(Result<bool, RimMindError>.Ok(true)); }
        }

        private sealed class CountingHandler : IToolHandler
        {
            private readonly bool _throw;
            private readonly ToolKind _kind;
            public CountingHandler(string id, bool throwOnExecute, ToolKind kind = ToolKind.Mutation)
            { Id = id; _throw = throwOnExecute; _kind = kind; }
            public string Id { get; }
            public string OwnerModId => "TestMod";
            public ToolDefinition Definition => new ToolDefinition
            {
                Id = Id,
                Manifest = new ToolManifest { Kind = _kind }
            };
            public int Calls { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                Calls++;
                if (_throw) throw new InvalidOperationException("boom");
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done", args.ToolCallId, Id)));
            }
        }

        private sealed class FakeRegistry : IToolRegistry
        {
            private readonly Dictionary<string, IToolHandler> _handlers;
            public FakeRegistry(params IToolHandler[] handlers) => _handlers = handlers.ToDictionary(h => h.Id);
            public void Register(IToolHandler handler) => _handlers[handler.Id] = handler;
            public bool Unregister(string toolId) => _handlers.Remove(toolId);
            public IToolHandler? FindById(string toolId) => _handlers.TryGetValue(toolId, out var h) ? h : null;
            public IReadOnlyList<IToolHandler> All => _handlers.Values.ToList();
            public IReadOnlyList<ToolDefinition> GetAllDefinitions() => _handlers.Values.Select(h => h.Definition).ToList();
            public IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind) => All;
            public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind) => GetAllDefinitions();
        }
    }
}
