using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    [Collection("UI drawing")]
    public sealed class PawnLookupContracts : IDisposable
    {
        public PawnLookupContracts()
        {
            RimMindPawnLookup.ClearCache();
            UnityData.IsInMainThread = true;
        }

        public void Dispose()
        {
            RimMindPawnLookup.ClearCache();
            UnityData.IsInMainThread = true;
        }

        [Fact]
        public void PawnLookup_respects_thread_safety_boundary_and_caching()
        {
            ContractCaseRunner.Run(
                ("main thread populates cache and finds pawn in map", (Action)(() =>
                {
                    UnityData.IsInMainThread = true;
                    var map = new Map();
                    var pawn = new Pawn { thingIDNumber = 42 };
                    map.mapPawns.FreeColonists.Add(pawn);
                    Find.CurrentMap = map;

                    var found = RimMindPawnLookup.FindPawnByNumber(42);
                    Assert.Same(pawn, found);

                    // Now switch off main thread: should find from cache without touching map
                    UnityData.IsInMainThread = false;
                    Find.CurrentMap = null; // Even if CurrentMap is gone or inaccessible
                    var cached = RimMindPawnLookup.FindPawnByNumber(42);
                    Assert.Same(pawn, cached);
                })),
                ("off-thread lookup of uncached pawn returns null without map access", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    // If off thread and not cached, MUST return null
                    var found = RimMindPawnLookup.FindPawnByNumber(999);
                    Assert.Null(found);

                    var foundById = RimMindPawnLookup.FindPawnById("Pawn_999");
                    Assert.Null(foundById);
                })),
                ("explicit CachePawn works off-thread", (Action)(() =>
                {
                    UnityData.IsInMainThread = false;
                    var pawn = new Pawn { thingIDNumber = 77 };
                    RimMindPawnLookup.CachePawn(pawn);

                    var found = RimMindPawnLookup.FindPawnByNumber(77);
                    Assert.Same(pawn, found);

                    var foundById = RimMindPawnLookup.FindPawnById("Pawn_77");
                    Assert.Same(pawn, foundById);
                })),
                ("GetEligibleColonists yields nothing off main thread", (Action)(() =>
                {
                    UnityData.IsInMainThread = false;
                    var map = new Map();
                    map.mapPawns.FreeColonistsSpawned.Add(new Pawn { thingIDNumber = 10, Map = map });
                    var colonists = RimMindPawnLookup.GetEligibleColonists(map).ToList();
                    Assert.Empty(colonists);
                })),
                ("IsActiveSpawnedColonist validates pawn liveness and map presence", (Action)(() =>
                {
                    UnityData.IsInMainThread = true;
                    var map = new Map();
                    var activePawn = new Pawn { thingIDNumber = 1, Map = map, Spawned = true, IsColonist = true, Faction = RimWorld.Faction.OfPlayer };
                    Assert.True(RimMindPawnLookup.IsActiveSpawnedColonist(activePawn));
                    Assert.True(RimMindPawnLookup.IsEligibleColonist(activePawn));

                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(null));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 2, Dead = true, Map = map, Spawned = true }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 3, Destroyed = true, Map = map, Spawned = true }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 4, Spawned = false, Map = map }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 5, Spawned = true, Map = null }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 6, Spawned = true, Map = map, IsColonist = false }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 7, Spawned = true, Map = map, IsPrisoner = true }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 8, Spawned = true, Map = map, IsSlave = true }));
                    Assert.False(RimMindPawnLookup.IsActiveSpawnedColonist(new Pawn { thingIDNumber = 9, Spawned = true, Map = map, Faction = null }));

                    // Downed and MentalState affect IsEligibleColonist but not IsActiveSpawnedColonist
                    var downedPawn = new Pawn { thingIDNumber = 11, Map = map, Spawned = true, IsColonist = true, Faction = RimWorld.Faction.OfPlayer, Downed = true };
                    Assert.True(RimMindPawnLookup.IsActiveSpawnedColonist(downedPawn));
                    Assert.False(RimMindPawnLookup.IsEligibleColonist(downedPawn));

                    var mentalPawn = new Pawn { thingIDNumber = 12, Map = map, Spawned = true, IsColonist = true, Faction = RimWorld.Faction.OfPlayer, MentalState = new object() };
                    Assert.True(RimMindPawnLookup.IsActiveSpawnedColonist(mentalPawn));
                    Assert.False(RimMindPawnLookup.IsEligibleColonist(mentalPawn));

                    map.mapPawns.FreeColonistsSpawned.Add(activePawn);
                    map.mapPawns.FreeColonistsSpawned.Add(downedPawn);
                    map.mapPawns.FreeColonistsSpawned.Add(mentalPawn);
                    var eligible = RimMindPawnLookup.GetEligibleColonists(map).ToList();
                    Assert.Single(eligible);
                    Assert.Same(activePawn, eligible[0]);
                })),
                ("IsActiveColonyAnimal validates domestic animal presence and player faction", (Action)(() =>
                {
                    UnityData.IsInMainThread = true;
                    var map = new Map();
                    var pet = new Pawn
                    {
                        thingIDNumber = 20,
                        Map = map,
                        Spawned = true,
                        IsColonist = false,
                        Faction = RimWorld.Faction.OfPlayer,
                        RaceProps = new RaceProperties { Animal = true }
                    };
                    Assert.True(RimMindPawnLookup.IsActiveColonyAnimal(pet));

                    // Human colonists are not colony animals
                    var colonist = new Pawn
                    {
                        thingIDNumber = 21,
                        Map = map,
                        Spawned = true,
                        IsColonist = true,
                        Faction = RimWorld.Faction.OfPlayer,
                        RaceProps = new RaceProperties { Animal = false }
                    };
                    Assert.False(RimMindPawnLookup.IsActiveColonyAnimal(colonist));

                    // Wild/enemy animal
                    var wildAnimal = new Pawn
                    {
                        thingIDNumber = 22,
                        Map = map,
                        Spawned = true,
                        Faction = null,
                        RaceProps = new RaceProperties { Animal = true }
                    };
                    Assert.False(RimMindPawnLookup.IsActiveColonyAnimal(wildAnimal));

                    // Dead animal
                    var deadPet = new Pawn
                    {
                        thingIDNumber = 23,
                        Map = map,
                        Spawned = true,
                        Dead = true,
                        Faction = RimWorld.Faction.OfPlayer,
                        RaceProps = new RaceProperties { Animal = true }
                    };
                    Assert.False(RimMindPawnLookup.IsActiveColonyAnimal(deadPet));

                    map.mapPawns.SpawnedColonyAnimals.Add(pet);
                    map.mapPawns.SpawnedColonyAnimals.Add(wildAnimal);
                    map.mapPawns.SpawnedColonyAnimals.Add(deadPet);

                    var activeAnimals = RimMindPawnLookup.GetActiveSpawnedColonyAnimals(map).ToList();
                    Assert.Single(activeAnimals);
                    Assert.Same(pet, activeAnimals[0]);

                    // FindPawnByNumber finds the colony animal
                    var foundAnimal = RimMindPawnLookup.FindPawnByNumber(20);
                    Assert.Same(pet, foundAnimal);
                }))
            );
        }

        [Fact]
        public void AgentActivityStreamDrawer_does_not_double_render_labels()
        {
            ContractCaseRunner.Run(
                ("DrawTraceRow has exactly one Widgets.Label call", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "UI", "AgentsPage", "AgentActivityStreamDrawer.cs");
                    string code = File.ReadAllText(path);

                    // In DrawTraceRow, Widgets.Label should be called only once (for the truncated label)
                    int labelCount = Regex.Matches(code, @"Widgets\.Label\(").Count;
                    Assert.Equal(1, labelCount);
                }))
            );
        }

        [Fact]
        public void PawnLookup_cache_eviction_and_dead_pawn_prevention_contracts()
        {
            ContractCaseRunner.Run(
                ("CachePawn rejects dead or destroyed pawns", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    var deadPawn = new Pawn { thingIDNumber = 101, Dead = true };
                    RimMindPawnLookup.CachePawn(deadPawn);
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(101));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_101"));

                    var destroyedPawn = new Pawn { thingIDNumber = 102, Destroyed = true };
                    RimMindPawnLookup.CachePawn(destroyedPawn);
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(102));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_102"));
                })),
                ("FindPawnByNumber and FindPawnById reject and evict dead cached pawns", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    var pawn = new Pawn { thingIDNumber = 201 };
                    RimMindPawnLookup.CachePawn(pawn);
                    Assert.Same(pawn, RimMindPawnLookup.FindPawnByNumber(201));
                    Assert.Same(pawn, RimMindPawnLookup.FindPawnById("Pawn_201"));

                    // Pawn dies in gameplay
                    pawn.Dead = true;

                    // Lookup must NOT return dead pawn and must cleanly evict
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(201));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_201"));
                })),
                ("FindPawnByNumber and FindPawnById reject and evict destroyed cached pawns", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    var pawn = new Pawn { thingIDNumber = 202 };
                    RimMindPawnLookup.CachePawn(pawn);
                    Assert.Same(pawn, RimMindPawnLookup.FindPawnByNumber(202));
                    Assert.Same(pawn, RimMindPawnLookup.FindPawnById("Pawn_202"));

                    // Pawn gets destroyed
                    pawn.Destroyed = true;

                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(202));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_202"));
                })),
                ("UncachePawn cleanly removes pawn from number and string caches", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    var pawn1 = new Pawn { thingIDNumber = 301 };
                    var pawn2 = new Pawn { thingIDNumber = 302 };
                    RimMindPawnLookup.CachePawn(pawn1);
                    RimMindPawnLookup.CachePawn(pawn2);

                    Assert.NotNull(RimMindPawnLookup.FindPawnByNumber(301));
                    Assert.NotNull(RimMindPawnLookup.FindPawnById("Pawn_301"));
                    Assert.NotNull(RimMindPawnLookup.FindPawnByNumber(302));
                    Assert.NotNull(RimMindPawnLookup.FindPawnById("Pawn_302"));

                    RimMindPawnLookup.UncachePawn(301);
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(301));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_301"));
                    Assert.NotNull(RimMindPawnLookup.FindPawnByNumber(302));

                    RimMindPawnLookup.UncachePawn("Pawn_302");
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(302));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_302"));
                })),
                ("ClearCache empties all registered pawns", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = false;

                    var pawn = new Pawn { thingIDNumber = 401 };
                    RimMindPawnLookup.CachePawn(pawn);
                    Assert.Same(pawn, RimMindPawnLookup.FindPawnByNumber(401));

                    RimMindPawnLookup.ClearCache();
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(401));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_401"));
                })),
                ("ExpiringNegativeCache suppresses rescans until TTL expires or Forget is called", (Action)(() =>
                {
                    long nowMs = 1000;
                    var negCache = new RimMind.Application.Common.Helpers.ExpiringNegativeCache<int>(
                        TimeSpan.FromSeconds(4),
                        maxEntries: 16,
                        clockMs: () => nowMs);

                    Assert.False(negCache.IsRecentMiss(501));
                    negCache.RecordMiss(501);
                    Assert.True(negCache.IsRecentMiss(501));

                    nowMs += 3999;
                    Assert.True(negCache.IsRecentMiss(501));

                    nowMs += 1;
                    Assert.False(negCache.IsRecentMiss(501));

                    negCache.RecordMiss(502);
                    Assert.True(negCache.IsRecentMiss(502));
                    negCache.Forget(502);
                    Assert.False(negCache.IsRecentMiss(502));
                })),
                ("FindPawnByNumber and FindPawnById negatively cache misses until CachePawn or ClearCache", (Action)(() =>
                {
                    RimMindPawnLookup.ClearCache();
                    UnityData.IsInMainThread = true;
                    var map = new Map();
                    Find.CurrentMap = map;

                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(601));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_601"));

                    var latePawn = new Pawn { thingIDNumber = 601 };
                    map.mapPawns.AllPawns.Add(latePawn);

                    // Suppressed by negative cache while within MissTtl
                    Assert.Null(RimMindPawnLookup.FindPawnByNumber(601));
                    Assert.Null(RimMindPawnLookup.FindPawnById("Pawn_601"));

                    // Explicit CachePawn forgets the negative cache entry immediately
                    RimMindPawnLookup.CachePawn(latePawn);
                    Assert.Same(latePawn, RimMindPawnLookup.FindPawnByNumber(601));
                    Assert.Same(latePawn, RimMindPawnLookup.FindPawnById("Pawn_601"));
                })),
                ("MechanismToolHelper.ResolvePawnId never substitutes fallbackId when explicit pawn_id is > 0", (Action)(() =>
                {
                    var extractor = new RimMind.Infrastructure.Mechanisms.MechanismToolHelper.FallbackJsonExtractor();
                    var explicitArgs = new RimMind.Application.Common.Models.Tools.ToolCallArgs
                    {
                        ArgumentsJson = "{\"pawn_id\":\"999\"}",
                        PawnId = 42,
                        NpcId = "Pawn_42"
                    };
                    Assert.Equal(999, RimMind.Infrastructure.Mechanisms.MechanismToolHelper.ResolvePawnId(extractor, explicitArgs));

                    var omittedArgs = new RimMind.Application.Common.Models.Tools.ToolCallArgs
                    {
                        ArgumentsJson = "{}",
                        PawnId = 42,
                        NpcId = "Pawn_42"
                    };
                    Assert.Equal(42, RimMind.Infrastructure.Mechanisms.MechanismToolHelper.ResolvePawnId(extractor, omittedArgs));
                }))
            );
        }

        [Fact]
        public void Lifecycle_and_dispatcher_static_invariants_contracts()
        {
            ContractCaseRunner.Run(
                ("GenScene_GameLifecyclePatch invokes RimMindPawnLookup.ClearCache", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Patches", "GenScene_GameLifecyclePatch.cs");
                    string code = File.ReadAllText(path);

                    Assert.Contains("RimMindPawnLookup.ClearCache()", code, StringComparison.Ordinal);
                })),
                ("AgentActiveChecker uses RimMindPawnLookup.FindPawnById and avoids PawnsFinder", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Verse", "AgentActiveChecker.cs");
                    string code = File.ReadAllText(path);

                    Assert.Contains("RimMindPawnLookup.FindPawnById", code, StringComparison.Ordinal);
                    Assert.DoesNotContain("PawnsFinder", code, StringComparison.Ordinal);
                })),
                ("JobActionDispatcher avoids WorldPawns traversal in FindPawnById", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Mechanisms", "Pawn", "Job", "JobActionDispatcher.cs");
                    string code = File.ReadAllText(path);

                    Assert.DoesNotContain("Find.WorldPawns", code, StringComparison.Ordinal);
                })),
                ("JobActionDispatcher HandleMoveTo verifies pawn.Map, InBounds, Fogged, Standable, and CanReach", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Mechanisms", "Pawn", "Job", "JobActionDispatcher.cs");
                    string code = File.ReadAllText(path);

                    Assert.Contains("pawn.Map", code, StringComparison.Ordinal);
                    Assert.Contains("cell.InBounds(map)", code, StringComparison.Ordinal);
                    Assert.Contains("cell.Fogged(map)", code, StringComparison.Ordinal);
                    Assert.Contains("cell.Standable(map)", code, StringComparison.Ordinal);
                    Assert.Contains("pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)", code, StringComparison.Ordinal);
                })),
                ("JobActionDispatcher HandleEatFood returns Err when food is null and checks CanReserveAndReach and WillIngestStackCountOf", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string path = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Mechanisms", "Pawn", "Job", "JobActionDispatcher.cs");
                    string code = File.ReadAllText(path);

                    Assert.Contains("if (food == null)", code, StringComparison.Ordinal);
                    Assert.Contains("Result<bool, RimMindError>.Err", code, StringComparison.Ordinal);
                    Assert.Contains("CanReserveAndReach", code, StringComparison.Ordinal);
                    Assert.Contains("FoodUtility.WillIngestStackCountOf", code, StringComparison.Ordinal);
                    Assert.Contains("RestUtility.FindBedFor(targetPawn, pawn", code, StringComparison.Ordinal);
                })),
                ("CompPawnAgent and NpcManager enforce IsActiveSpawnedColonist and UncachePawn lifecycle cleanup", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string compCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Verse", "CompPawnAgent.cs"));
                    string npcCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Verse", "NpcManager.cs"));

                    Assert.Contains("RimMindPawnLookup.IsActiveSpawnedColonist(pawn)", compCode, StringComparison.Ordinal);
                    Assert.Contains("RimMindPawnLookup.UncachePawn(pawn)", compCode, StringComparison.Ordinal);
                    Assert.Contains("RimMindPawnLookup.UncachePawn(thingId)", npcCode, StringComparison.Ordinal);
                })),
                ("MechanismToolHandler populates ToolDefinition.Manifest with RiskLevel and RequiresApproval", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string handlerCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Mechanisms", "MechanismToolHandler.cs"));

                    Assert.Contains("Manifest = new ToolManifest", handlerCode, StringComparison.Ordinal);
                    Assert.Contains("RiskLevel = MapRisk(mechanismRisk)", handlerCode, StringComparison.Ordinal);
                    Assert.Contains("RequiresApproval = mechanismRisk >= MechanismRisk.Dangerous", handlerCode, StringComparison.Ordinal);
                })),
                ("Player2Client uses SharedHttpClient off main thread and RimMindRuntimeGameComponent stops health check", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string p2Dir = Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Services", "Clients", "Player2");
                    foreach (var file in new[] { "Player2Client.Discovery.cs", "Player2Client.HealthCheck.cs", "Player2Client.Balance.cs", "Player2Client.RawRequests.cs" })
                    {
                        string code = File.ReadAllText(Path.Combine(p2Dir, file));
                        Assert.DoesNotContain("UnityWebRequest", code, StringComparison.Ordinal);
                        Assert.Contains("SharedHttpClient", code, StringComparison.Ordinal);
                    }

                    string runtimeCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Presentation", "Runtime", "RimMindRuntimeGameComponent.cs"));
                    Assert.Contains("Player2Client.StopHealthCheck()", runtimeCode, StringComparison.Ordinal);
                })),
                ("VersePawnSleepDetector and Window_AgentProgressFloat follow pawn lookup and thread-safety invariants", (Action)(() =>
                {
                    string root = FindRepoRoot();
                    string sleepCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "Social", "VersePawnSleepDetector.cs"));
                    Assert.Contains("RimMindPawnLookup.FindPawnById", sleepCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("WorldPawns", sleepCode, StringComparison.Ordinal);

                    string floatCode = File.ReadAllText(Path.Combine(root, "RimMind-Core", "Source", "Infrastructure", "UI", "Window_AgentProgressFloat.cs"));
                    Assert.Contains("RimMindPawnLookup.GetEligibleColonists(Find.CurrentMap)", floatCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("Find.CurrentMap.mapPawns.AllPawns", floatCode, StringComparison.Ordinal);
                    Assert.Contains("_entriesDirty = true", floatCode, StringComparison.Ordinal);
                }))
            );
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RimMind.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root");
        }
    }
}
