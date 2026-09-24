using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Llm;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class NpcEnrichMiddlewareContract
    {
        [Fact]
        public async Task Enrichment_is_best_effort_and_does_not_manage_npc_lifecycle()
        {
            await ContractCaseRunner.RunAsync(
                ("missing profile continues the request", async () =>
                {
                    var manager = new RecordingNpcManager();
                    var context = CreateContext("NPC-783");
                    var nextCalled = false;

                    await new NpcEnrichMiddleware(manager).InvokeAsync(context, _ =>
                    {
                        nextCalled = true;
                        return Task.CompletedTask;
                    });

                    Assert.True(nextCalled);
                    Assert.False(context.IsShortCircuited);
                    Assert.Null(context.Result);
                    Assert.False(context.Items.ContainsKey("NpcProfile"));
                    Assert.Equal(0, manager.SpawnCount);
                }),
                ("available profile enriches without lifecycle mutation", async () =>
                {
                    var profile = new NpcProfile("NPC-42", 42, "Ada");
                    var manager = new RecordingNpcManager(profile);
                    var context = CreateContext(profile.NpcId);
                    var nextCalled = false;

                    await new NpcEnrichMiddleware(manager).InvokeAsync(context, _ =>
                    {
                        nextCalled = true;
                        return Task.CompletedTask;
                    });

                    Assert.True(nextCalled);
                    Assert.False(context.IsShortCircuited);
                    Assert.Same(profile, context.Items["NpcProfile"]);
                    Assert.Equal(0, manager.SpawnCount);
                }));
        }

        private static LlmRequestContext CreateContext(string npcId)
        {
            return new LlmRequestContext(new LlmRequestEnvelope
            {
                RequestId = "npc-enrich-contract",
                ModId = "RimMind.Tests",
                NpcId = npcId,
            });
        }

        private sealed class RecordingNpcManager : INpcManager
        {
            private readonly NpcProfile? _profile;

            public RecordingNpcManager(NpcProfile? profile = null)
            {
                _profile = profile;
            }

            public int SpawnCount { get; private set; }

            public void SpawnNpc(NpcProfile profile) => SpawnCount++;
            public void KillNpc(string npcId) { }
            public bool IsNpcAlive(string npcId) => false;
            public NpcProfile? GetNpc(string npcId) => _profile?.NpcId == npcId ? _profile : null;
            public IReadOnlyList<NpcProfile> GetAllNpcs() => _profile == null ? new List<NpcProfile>() : new[] { _profile };
            public string GetNpcForMap(object map) => string.Empty;
            public object? FindPawnByNpcId(string npcId) => null;
            public object? FindProxyPawnForMap(object map) => null;
            public void RegisterActiveAgent(int thingId) { }
            public void UnregisterActiveAgent(int thingId) { }
            public HashSet<int> GetActiveAgentPawnIds() => new HashSet<int>();
            public void IndexPawn(object pawn) { }
            public void UnindexPawn(int thingId) { }
            public string GetMapNpcId(object map) => string.Empty;
        }
    }
}
