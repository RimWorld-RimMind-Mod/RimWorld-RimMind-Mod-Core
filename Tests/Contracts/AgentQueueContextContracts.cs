using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Context.Diff;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Application.Features.Utility;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class AgentQueueContextContracts
    {
        [Fact]
        public void Ring_buffer_preserves_recent_ordered_history()
        {
            ContractCaseRunner.Run(
                ("new buffer is empty", () => Assert.Empty(new RingBuffer<int>(3).AsEnumerable())),
                ("append increments count", () =>
                {
                    var buffer = new RingBuffer<int>(3);
                    buffer.Add(1);
                    buffer.Add(2);
                    Assert.Equal(2, buffer.Count);
                }),
                ("items remain insertion ordered before capacity", () =>
                {
                    var buffer = new RingBuffer<int>(4);
                    buffer.Add(10);
                    buffer.Add(20);
                    buffer.Add(30);
                    Assert.Equal(new[] { 10, 20, 30 }, buffer.AsEnumerable());
                }),
                ("overflow discards the oldest item", () =>
                {
                    var buffer = new RingBuffer<int>(3);
                    foreach (int value in new[] { 1, 2, 3, 4, 5 })
                    {
                        buffer.Add(value);
                    }
                    Assert.Equal(new[] { 3, 4, 5 }, buffer.AsEnumerable());
                }),
                ("count remains capacity bounded", () =>
                {
                    var buffer = new RingBuffer<int>(2);
                    foreach (int value in new[] { 1, 2, 3, 4 })
                    {
                        buffer.Add(value);
                    }
                    Assert.Equal(2, buffer.Count);
                }),
                ("single-slot buffer always exposes the latest item", () =>
                {
                    var buffer = new RingBuffer<int>(1);
                    buffer.Add(42);
                    buffer.Add(99);
                    Assert.Equal(new[] { 99 }, buffer.AsEnumerable());
                }));
        }

        [Fact]
        public void Context_diff_computation_reports_external_changes()
        {
            ContractCaseRunner.Run(
                ("equal snapshots produce no diff", () =>
                {
                    var values = new Dictionary<string, string> { ["Health"] = "100" };
                    Assert.Empty(new DiffComputer().Compute(values, values, ContextLayer.L1_Baseline));
                }),
                ("changed value preserves old and new values", () =>
                {
                    var diffs = new DiffComputer().Compute(
                        new Dictionary<string, string> { ["Health"] = "100" },
                        new Dictionary<string, string> { ["Health"] = "75" },
                        ContextLayer.L1_Baseline);
                    Assert.Collection(diffs, diff =>
                    {
                        Assert.Equal("Health", diff.Key);
                        Assert.Equal("100", diff.OldValue);
                        Assert.Equal("75", diff.NewValue);
                    });
                }),
                ("new key has an empty old value", () =>
                {
                    var diffs = new DiffComputer().Compute(
                        new Dictionary<string, string>(),
                        new Dictionary<string, string> { ["Mood"] = "80" },
                        ContextLayer.L3_State);
                    Assert.Equal(string.Empty, Assert.Single(diffs).OldValue);
                }),
                ("removed key has an empty new value", () =>
                {
                    var diffs = new DiffComputer().Compute(
                        new Dictionary<string, string> { ["Mood"] = "80" },
                        new Dictionary<string, string>(),
                        ContextLayer.L2_Environment);
                    Assert.Equal(string.Empty, Assert.Single(diffs).NewValue);
                }),
                ("null snapshot is treated as unavailable rather than a mass change", () =>
                {
                    var computer = new DiffComputer();
                    Assert.Empty(computer.Compute(null!, new Dictionary<string, string>(), ContextLayer.L1_Baseline));
                    Assert.Empty(computer.Compute(new Dictionary<string, string>(), null!, ContextLayer.L1_Baseline));
                }),
                ("multiple changes retain their requested layer", () =>
                {
                    var diffs = new DiffComputer().Compute(
                        new Dictionary<string, string> { ["Health"] = "100", ["Mood"] = "80" },
                        new Dictionary<string, string> { ["Health"] = "75", ["Energy"] = "90" },
                        ContextLayer.L4_History);
                    Assert.Equal(3, diffs.Count);
                    Assert.All(diffs, diff => Assert.Equal(ContextLayer.L4_History, diff.Layer));
                }));
        }

        [Fact]
        public async Task Provider_cache_obeys_freshness_and_invalidation()
        {
            await ContractCaseRunner.RunAsync(
                ("zero staleness computes every request", async () =>
                {
                    var tick = new ManualTickProvider { CurrentTick = 100 };
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: tick);
                    ContextProviderDef definition = Definition(
                        "volatile",
                        0,
                        (_, _) => Task.FromResult<string?>($"value-{++calls}"));
                    await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    Assert.Equal(2, calls);
                }),
                ("fresh entry is reused", async () =>
                {
                    var tick = new ManualTickProvider { CurrentTick = 100 };
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: tick);
                    ContextProviderDef definition = Definition(
                        "fresh",
                        600,
                        (_, _) => Task.FromResult<string?>($"value-{++calls}"));
                    string? first = await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    tick.CurrentTick = 699;
                    string? second = await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    Assert.Equal(first, second);
                    Assert.Equal(1, calls);
                }),
                ("freshness boundary recomputes", async () =>
                {
                    var tick = new ManualTickProvider { CurrentTick = 100 };
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: tick);
                    ContextProviderDef definition = Definition(
                        "boundary",
                        600,
                        (_, _) => Task.FromResult<string?>($"value-{++calls}"));
                    await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    tick.CurrentTick = 700;
                    await cache.GetOrComputeAsync(definition, Context(), CancellationToken.None);
                    Assert.Equal(2, calls);
                }),
                ("key invalidation removes every scoped entry for that key", async () =>
                {
                    var tick = new ManualTickProvider { CurrentTick = 100 };
                    var cache = new ProviderCache(tickProvider: tick);
                    ContextProviderDef first = Definition("first", 6000, (_, _) => Task.FromResult<string?>("a"), CacheScope.Pawn);
                    ContextProviderDef second = Definition("second", 6000, (_, _) => Task.FromResult<string?>("b"), CacheScope.Pawn);
                    await cache.GetOrComputeAsync(first, Context("npc-1", 1), CancellationToken.None);
                    await cache.GetOrComputeAsync(first, Context("npc-2", 2), CancellationToken.None);
                    await cache.GetOrComputeAsync(second, Context("npc-1", 1), CancellationToken.None);
                    cache.InvalidateKey("first");
                    Assert.Equal(1, cache.Count);
                }),
                ("clear removes all cached entries", async () =>
                {
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    await cache.GetOrComputeAsync(
                        Definition("clear", 6000, (_, _) => Task.FromResult<string?>("value")),
                        Context(),
                        CancellationToken.None);
                    cache.Clear();
                    Assert.Equal(0, cache.Count);
                }));
        }

        [Fact]
        public async Task Provider_cache_scopes_are_identity_safe()
        {
            await ContractCaseRunner.RunAsync(
                ("static scope is shared across requests", async () =>
                {
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    ContextProviderDef definition = Definition(
                        "static",
                        600,
                        (context, _) => Task.FromResult<string?>($"{context.NpcId}-{++calls}"),
                        CacheScope.Static);
                    string? first = await cache.GetOrComputeAsync(definition, Context("npc-a", 1, "dialogue", 1), CancellationToken.None);
                    string? second = await cache.GetOrComputeAsync(definition, Context("npc-b", 2, "advisor", 2), CancellationToken.None);
                    Assert.Equal(first, second);
                    Assert.Equal(1, calls);
                }),
                ("scenario scope separates scenario identities", async () =>
                {
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    ContextProviderDef definition = Definition(
                        "scenario",
                        600,
                        (context, _) => Task.FromResult<string?>($"{context.Scenario}-{++calls}"));
                    string? dialogue = await cache.GetOrComputeAsync(definition, Context(scenario: "dialogue"), CancellationToken.None);
                    string? advisor = await cache.GetOrComputeAsync(definition, Context(scenario: "advisor"), CancellationToken.None);
                    Assert.NotEqual(dialogue, advisor);
                }),
                ("pawn scope separates pawn identities", async () =>
                {
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    ContextProviderDef definition = Definition(
                        "pawn",
                        600,
                        (context, _) => Task.FromResult<string?>($"{context.PawnId}-{++calls}"),
                        CacheScope.Pawn);
                    string? first = await cache.GetOrComputeAsync(definition, Context("npc-a", 1), CancellationToken.None);
                    string? second = await cache.GetOrComputeAsync(definition, Context("npc-b", 2), CancellationToken.None);
                    Assert.NotEqual(first, second);
                }),
                ("map scope separates maps while sharing the no-map identity", async () =>
                {
                    int calls = 0;
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    ContextProviderDef definition = Definition(
                        "map",
                        600,
                        (context, _) => Task.FromResult<string?>($"{context.MapId?.ToString() ?? "none"}-{++calls}"),
                        CacheScope.Map);
                    string? mapOne = await cache.GetOrComputeAsync(definition, Context(mapId: 1), CancellationToken.None);
                    string? mapTwo = await cache.GetOrComputeAsync(definition, Context(mapId: 2), CancellationToken.None);
                    string? noMapOne = await cache.GetOrComputeAsync(definition, Context("npc-a", mapId: null), CancellationToken.None);
                    string? noMapTwo = await cache.GetOrComputeAsync(definition, Context("npc-b", mapId: null), CancellationToken.None);
                    Assert.NotEqual(mapOne, mapTwo);
                    Assert.Equal(noMapOne, noMapTwo);
                }),
                ("provider failure returns no cached value", async () =>
                {
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    string? value = await cache.GetOrComputeAsync(
                        Definition("failure", 600, (_, _) => throw new InvalidOperationException("provider failure")),
                        Context(),
                        CancellationToken.None);
                    Assert.Null(value);
                    Assert.Equal(0, cache.Count);
                }),
                ("pre-cancelled request remains cancelled", async () =>
                {
                    var cache = new ProviderCache(tickProvider: new ManualTickProvider());
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    await Assert.ThrowsAsync<OperationCanceledException>(() =>
                        cache.GetOrComputeAsync(
                            Definition("cancel", 600, (_, _) => Task.FromResult<string?>("value")),
                            Context(),
                            cancellation.Token));
                }));
        }

        [Fact]
        public async Task Request_queue_controls_execution_and_completion()
        {
            await ContractCaseRunner.RunAsync(
                ("paused queue waits until resume", async () =>
                {
                    var queue = new RequestQueue();
                    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    queue.PauseQueue();
                    queue.Enqueue(
                        Envelope("paused"),
                        _ => { },
                        _ =>
                        {
                            started.TrySetResult(true);
                            return Task.FromResult(Success());
                        });
                    Assert.False(started.Task.IsCompleted);
                    queue.ResumeQueue();
                    queue.CurrentTick = 60;
                    queue.Tick();
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                }),
                ("completion is delivered by queue tick", async () =>
                {
                    var queue = new RequestQueue();
                    var callback = new TaskCompletionSource<Result<LlmResponse, RimMindError>>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    queue.Enqueue(Envelope("complete"), result => callback.TrySetResult(result), _ => Task.FromResult(Success()));
                    await WaitUntilAsync(() => queue.PendingCallbackCount > 0);
                    queue.Tick();
                    Result<LlmResponse, RimMindError> result = await callback.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.True(result.IsOk);
                }),
                ("active cancellation invokes callback exactly once", async () =>
                {
                    var queue = new RequestQueue();
                    int callbacks = 0;
                    queue.Enqueue(
                        Envelope("cancel"),
                        _ => Interlocked.Increment(ref callbacks),
                        async cancellation =>
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                            return Success();
                        });
                    Assert.True(queue.CancelRequest("cancel"));
                    await WaitUntilAsync(() => queue.PendingCallbackCount > 0);
                    queue.Tick();
                    queue.Tick();
                    Assert.Equal(1, callbacks);
                }),
                ("expired queued request completes with timeout", () =>
                {
                    var queue = new RequestQueue { CurrentTick = 0 };
                    Result<LlmResponse, RimMindError>? callback = null;
                    queue.PauseQueue();
                    queue.Enqueue(
                        new LlmRequestEnvelope
                        {
                            RequestId = "expired",
                            ModId = "Contracts",
                            ExpireAtTicks = 1
                        },
                        result => callback = result,
                        _ => Task.FromResult(Success()));
                    queue.ResumeQueue();
                    queue.CurrentTick = 60;
                    queue.Tick();
                    queue.Tick();
                    Assert.NotNull(callback);
                    Assert.Equal(RimMindErrorCode.Timeout, callback!.Value.Error.Code);
                    return Task.CompletedTask;
                }),
                ("unknown cancellation leaves queue unchanged", () =>
                {
                    var queue = new RequestQueue();
                    Assert.False(queue.CancelRequest("missing"));
                    Assert.Equal(0, queue.TotalQueuedCount);
                    Assert.Equal(0, queue.ActiveRequestCount);
                    return Task.CompletedTask;
                }),
                ("queue reset clears pending callbacks and does not deliver them on next tick", async () =>
                {
                    var queue = new RequestQueue();
                    int callbacks = 0;
                    queue.Enqueue(
                        Envelope("reset-test"),
                        _ => Interlocked.Increment(ref callbacks),
                        _ => Task.FromResult(Success()));
                    await WaitUntilAsync(() => queue.PendingCallbackCount > 0);
                    Assert.True(queue.PendingCallbackCount > 0);
                    queue.Reset();
                    Assert.Equal(0, queue.PendingCallbackCount);
                    queue.Tick();
                    Assert.Equal(0, callbacks);
                }));
        }

        private static ContextProviderDef Definition(
            string key,
            int stalenessTicks,
            Func<ProviderContext, CancellationToken, Task<string?>> provider,
            CacheScope scope = CacheScope.Scenario)
        {
            return new ContextProviderDef(
                key,
                ContextLayer.L2_Environment,
                1.0f,
                provider,
                stalenessTicks: stalenessTicks,
                cacheScope: scope);
        }

        private static ProviderContext Context(
            string npcId = "npc-1",
            int pawnId = 1,
            string scenario = "dialogue",
            int? mapId = null)
        {
            return new ProviderContext(scenario, "trace")
            {
                NpcId = npcId,
                PawnId = pawnId,
                MapId = mapId
            };
        }

        private static LlmRequestEnvelope Envelope(string requestId)
        {
            return new LlmRequestEnvelope
            {
                RequestId = requestId,
                ModId = "Contracts"
            };
        }

        private static Result<LlmResponse, RimMindError> Success()
        {
            return Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "ok" });
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!predicate())
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private sealed class ManualTickProvider : ITickProvider
        {
            public int CurrentTick { get; set; }
            public int TicksGame => CurrentTick;
        }
    }
}
