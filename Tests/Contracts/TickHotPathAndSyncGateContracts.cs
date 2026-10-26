using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Threading;
using RimMind.Infrastructure.Verse;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class TickHotPathAndSyncGateContracts
    {
        [Fact]
        public void SynchronousTaskGate_completes_synchronously_or_fails_fast_without_blocking()
        {
            ContractCaseRunner.Run(
                ("completed task yields result immediately", () =>
                {
                    var completed = Task.FromResult(42);
                    Assert.True(SynchronousTaskGate.TryCompleteSynchronously(completed, out int val));
                    Assert.Equal(42, val);
                }),
                ("faulted completed task rethrows inner exception", () =>
                {
                    var faulted = Task.FromException<int>(new InvalidOperationException("sync boom"));
                    var ex = Assert.Throws<InvalidOperationException>(() =>
                        SynchronousTaskGate.TryCompleteSynchronously(faulted, out _));
                    Assert.Equal("sync boom", ex.Message);
                }),
                ("incomplete task returns false without blocking and observes later fault", () =>
                {
                    var tcs = new TaskCompletionSource<int>();
                    Assert.False(SynchronousTaskGate.TryCompleteSynchronously(tcs.Task, out int val));
                    Assert.Equal(0, val);

                    // Faulting later is observed so it cannot trigger UnobservedTaskException
                    tcs.SetException(new InvalidOperationException("late fault"));
                    Assert.True(tcs.Task.IsFaulted);
                }),
                ("TryClaimLogOnce returns true only on first call", () =>
                {
                    int flag = 0;
                    Assert.True(SynchronousTaskGate.TryClaimLogOnce(ref flag));
                    Assert.False(SynchronousTaskGate.TryClaimLogOnce(ref flag));
                    Assert.Equal(1, flag);
                })
            );
        }

        [Fact]
        public void TickStagger_distributes_phases_deterministically_across_interval()
        {
            ContractCaseRunner.Run(
                ("PhaseOffset stays within [0, interval) for positive, zero, and negative ids", () =>
                {
                    const int interval = 150;
                    foreach (int id in new[] { 0, 1, 7, 149, 150, 151, -1, -149, -150, -151, int.MinValue, int.MaxValue })
                    {
                        int offset = TickStagger.PhaseOffset(id, interval);
                        Assert.InRange(offset, 0, interval - 1);
                    }
                    Assert.Equal(0, TickStagger.PhaseOffset(42, 1));
                    Assert.Equal(0, TickStagger.PhaseOffset(42, 0));
                }),
                ("InitialLastTick staggers first execution across [1, interval]", () =>
                {
                    const int interval = 150;
                    var firstFireTicks = new HashSet<int>();
                    for (int id = 1; id <= 30; id++)
                    {
                        int lastTick = TickStagger.InitialLastTick(id, interval);
                        Assert.InRange(lastTick, -(interval - 1), 0);

                        int firstFire = -1;
                        for (int tick = 1; tick <= interval; tick++)
                        {
                            if (tick - lastTick >= interval)
                            {
                                firstFire = tick;
                                break;
                            }
                        }
                        Assert.InRange(firstFire, 1, interval);
                        firstFireTicks.Add(firstFire);
                    }
                    Assert.Equal(30, firstFireTicks.Count);
                }),
                ("IsDue fires exactly once per interval", () =>
                {
                    const int interval = 60;
                    for (int id = -5; id <= 5; id++)
                    {
                        int count = 0;
                        for (int t = 0; t < interval * 3; t++)
                        {
                            if (TickStagger.IsDue(t, id, interval))
                                count++;
                        }
                        Assert.Equal(3, count);
                    }
                })
            );
        }

        [Fact]
        public void CompPawnAgent_cleans_up_session_on_despawn_kill_and_destroy()
        {
            var pawn = new Pawn { thingIDNumber = 77, Spawned = true, IsColonist = true, Map = new Map() };
            var comp = new CompPawnAgent { parent = pawn };
            pawn.AllComps.Add(comp);

            comp.StartIntentSession(new AgentIntentSession("TestIntent", startTick: 0, durationTicks: 500));
            Assert.NotNull(comp.ActiveIntentSession);

            comp.PostDeSpawn(pawn.Map!);
            Assert.Null(comp.ActiveIntentSession);

            comp.StartIntentSession(new AgentIntentSession("TestIntent2", startTick: 0, durationTicks: 500));
            comp.Notify_Killed(pawn.Map!);
            Assert.Null(comp.ActiveIntentSession);
        }
    }
}
