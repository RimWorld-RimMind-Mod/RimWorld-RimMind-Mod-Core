using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class CircuitBreakerMiddlewareContract
    {
        [Fact]
        public async Task Cancellation_error_does_not_trip_circuit_breaker()
        {
            Func<Task> case1 = async () =>
            {
                var settings = new StubCircuitBreakerSettings(threshold: 2, openDurationSec: 1);
                var middleware = new CircuitBreakerMiddleware(settings);

                for (int i = 0; i < 5; i++)
                {
                    var context = CreateContext($"req-cancel-{i}");
                    await middleware.InvokeAsync(context, ctx =>
                    {
                        var err = (i % 3) switch
                        {
                            0 => RimMindErrors.Cancelled(),
                            1 => RimMindErrors.ClientNotConfigured("No API key"),
                            _ => RimMindErrors.PipelineShortCircuited("dedup")
                        };
                        ctx.Result = Result<LlmResponse, RimMindError>.Err(err);
                        return Task.CompletedTask;
                    });

                    Assert.Equal(0, middleware.ConsecutiveFailuresCount);
                    Assert.Equal("Closed", middleware.CurrentStateName);
                    Assert.False(context.IsShortCircuited);
                }
            };

            Func<Task> case2 = async () =>
            {
                var settings = new StubCircuitBreakerSettings(threshold: 2, openDurationSec: 10);
                var middleware = new CircuitBreakerMiddleware(settings);

                // First failure
                var ctx1 = CreateContext("req-fail-1");
                await middleware.InvokeAsync(ctx1, ctx =>
                {
                    ctx.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("fail 1"));
                    return Task.CompletedTask;
                });
                Assert.Equal(1, middleware.ConsecutiveFailuresCount);
                Assert.Equal("Closed", middleware.CurrentStateName);

                // Second failure -> trips to Open
                var ctx2 = CreateContext("req-fail-2");
                await middleware.InvokeAsync(ctx2, ctx =>
                {
                    ctx.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("fail 2"));
                    return Task.CompletedTask;
                });
                Assert.Equal(2, middleware.ConsecutiveFailuresCount);
                Assert.Equal("Open", middleware.CurrentStateName);

                // Third request -> short-circuited
                var ctx3 = CreateContext("req-fail-3");
                bool nextCalled = false;
                await middleware.InvokeAsync(ctx3, _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                });
                Assert.False(nextCalled);
                Assert.True(ctx3.IsShortCircuited);
                Assert.Equal("circuit_open", ctx3.ShortCircuitReason);
                Assert.Equal(RimMindErrorCode.ClientCircuitOpen, ctx3.Result!.Value.Error.Code);
            };

            await ContractCaseRunner.RunAsync(
                ("cancellation does not increment failure count", case1),
                ("actual errors trip the circuit breaker at threshold", case2));
        }

        [Fact]
        public async Task HalfOpen_allows_single_canary_probe()
        {
            await ContractCaseRunner.RunAsync(
                ("half-open admits only one probe and blocks concurrent requests", async () =>
                {
                    // Threshold 1, Open duration 0s (transitions to HalfOpen on next request)
                    var settings = new StubCircuitBreakerSettings(threshold: 1, openDurationSec: 0);
                    var middleware = new CircuitBreakerMiddleware(settings);

                    // Trip circuit
                    var tripCtx = CreateContext("req-trip");
                    await middleware.InvokeAsync(tripCtx, ctx =>
                    {
                        ctx.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("trip"));
                        return Task.CompletedTask;
                    });
                    Assert.Equal("Open", middleware.CurrentStateName);

                    var canaryStarted = new TaskCompletionSource<bool>();
                    var canaryBlocker = new TaskCompletionSource<bool>();

                    // Launch canary probe in background
                    var canaryCtx = CreateContext("req-canary");
                    var canaryTask = Task.Run(async () =>
                    {
                        await middleware.InvokeAsync(canaryCtx, async ctx =>
                        {
                            canaryStarted.SetResult(true);
                            await canaryBlocker.Task;
                            ctx.Result = Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "canary-ok" });
                        });
                    });

                    await Task.WhenAny(canaryStarted.Task, Task.Delay(5000));
                    Assert.True(canaryStarted.Task.IsCompleted, "Canary probe failed to start within timeout");

                    // Concurrent request arrives while canary is in progress
                    var concurrentCtx = CreateContext("req-concurrent");
                    bool concurrentNextCalled = false;
                    await middleware.InvokeAsync(concurrentCtx, _ =>
                    {
                        concurrentNextCalled = true;
                        return Task.CompletedTask;
                    });

                    Assert.False(concurrentNextCalled);
                    Assert.True(concurrentCtx.IsShortCircuited);
                    Assert.Equal("circuit_open", concurrentCtx.ShortCircuitReason);

                    // Let canary complete
                    canaryBlocker.SetResult(true);
                    await Task.WhenAny(canaryTask, Task.Delay(5000));
                    Assert.True(canaryTask.IsCompleted, "Canary task failed to complete within timeout");

                    // Circuit should now be Closed
                    Assert.Equal("Closed", middleware.CurrentStateName);
                    Assert.Equal(0, middleware.ConsecutiveFailuresCount);
                }));
        }

        private static LlmRequestContext CreateContext(string requestId)
        {
            return new LlmRequestContext(new LlmRequestEnvelope
            {
                RequestId = requestId,
                ModId = "TestMod",
            });
        }

        private sealed class StubCircuitBreakerSettings : ICircuitBreakerSettings
        {
            public int CircuitBreakerFailureThreshold { get; }
            public int CircuitBreakerOpenDurationSec { get; }

            public StubCircuitBreakerSettings(int threshold, int openDurationSec)
            {
                CircuitBreakerFailureThreshold = threshold;
                CircuitBreakerOpenDurationSec = openDurationSec;
            }
        }
    }
}
