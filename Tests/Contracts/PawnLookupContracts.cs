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
                    map.mapPawns.FreeColonists.Add(new Pawn { thingIDNumber = 10 });
                    var colonists = RimMindPawnLookup.GetEligibleColonists(map).ToList();
                    Assert.Empty(colonists);
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
