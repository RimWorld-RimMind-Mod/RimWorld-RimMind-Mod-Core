using System.Linq;
using RimMind.Presentation.Agent;
using RimWorld;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    /// <summary>
    /// Regression: PawnAgent never perceived hostiles (ThreatCount was hard-coded to 0), so a mad
    /// gazelle / mad dromedary wiped colonies in live playthroughs without any draft or flee decision.
    /// </summary>
    public sealed class ThreatScannerContracts
    {
        [Fact]
        public void FindsAggroAnimalsAndHostileFactionPawns_IgnoresFriendlyDownedAndFar()
        {
            var map = new Map();
            var self = new Pawn { thingIDNumber = 1, Position = new IntVec3(10, 0, 10), Map = map };
            var raiderFaction = new Faction { HostileToPlayer = true };

            var madAnimal = new Pawn { thingIDNumber = 2, Position = new IntVec3(15, 0, 10), Map = map, Faction = null, InAggroMentalState = true, KindLabel = "dromedary" };
            var raider = new Pawn { thingIDNumber = 3, Position = new IntVec3(30, 0, 10), Map = map, Faction = raiderFaction, KindLabel = "raider" };
            var friend = new Pawn { thingIDNumber = 4, Position = new IntVec3(11, 0, 10), Map = map };
            var downedRaider = new Pawn { thingIDNumber = 5, Position = new IntVec3(12, 0, 10), Map = map, Faction = raiderFaction, Downed = true };
            var farRaider = new Pawn { thingIDNumber = 6, Position = new IntVec3(200, 0, 10), Map = map, Faction = raiderFaction };

            map.mapPawns.AllPawnsSpawned.AddRange(new[] { self, madAnimal, raider, friend, downedRaider, farRaider });

            var threats = ThreatScanner.FindNearbyThreats(self);

            Assert.Equal(new[] { 2, 3 }, threats.Select(t => t.thingIDNumber).ToArray());
            var text = ThreatScanner.Describe(self, threats);
            Assert.Contains("dromedary (5 tiles)", text);
            Assert.Contains("raider (20 tiles)", text);
        }

        [Fact]
        public void NoThreats_YieldsEmptyDescription()
        {
            var map = new Map();
            var self = new Pawn { thingIDNumber = 1, Map = map };
            map.mapPawns.AllPawnsSpawned.Add(self);

            Assert.Empty(ThreatScanner.FindNearbyThreats(self));
            Assert.Equal(string.Empty, ThreatScanner.Describe(self, ThreatScanner.FindNearbyThreats(self)));
        }
    }
}
