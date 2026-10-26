using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Tests.Contracts
{
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

        private sealed class CountingHandler : IToolHandler
        {
            private readonly bool _throw;
            public CountingHandler(string id, bool throwOnExecute) { Id = id; _throw = throwOnExecute; }
            public string Id { get; }
            public string OwnerModId => "TestMod";
            public ToolDefinition Definition => new ToolDefinition { Id = Id };
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
