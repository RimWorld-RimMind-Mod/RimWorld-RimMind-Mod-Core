using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Shared scan for active hostiles near a pawn (raiders, manhunters, mad/berserk creatures).
    /// Main thread only: enumerates <c>mapPawns</c>.
    /// </summary>
    public static class ThreatScanner
    {
        public const float DefaultRadius = 45f;

        public static List<Pawn> FindNearbyThreats(Pawn pawn, float radius = DefaultRadius)
        {
            var result = new List<Pawn>();
            var map = pawn?.Map;
            if (pawn == null || map?.mapPawns == null || !UnityData.IsInMainThread) return result;

            float radiusSq = radius * radius;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || p == pawn || p.Dead || p.Downed) continue;
                if (p.Position.Fogged(map)) continue;
                if (!(p.InAggroMentalState || (p.Faction != null && p.HostileTo(Faction.OfPlayer)))) continue;
                if (p.Position.DistanceToSquared(pawn.Position) > radiusSq) continue;
                result.Add(p);
            }
            result.Sort((a, b) => a.Position.DistanceToSquared(pawn.Position).CompareTo(b.Position.DistanceToSquared(pawn.Position)));
            return result;
        }

        /// <summary>Compact description, e.g. "manhunter dromedary (12 tiles), raider x3 (40 tiles)".</summary>
        public static string Describe(Pawn pawn, IReadOnlyList<Pawn> threats, int maxGroups = 4)
        {
            if (pawn == null || threats == null || threats.Count == 0) return string.Empty;
            return string.Join(", ", threats
                .GroupBy(p => ((p.InAggroMentalState ? (p.MentalStateDef?.label ?? string.Empty) + " " : string.Empty) + p.KindLabel).Trim())
                .Take(maxGroups)
                .Select(g =>
                {
                    int dist = (int)g.Min(p => p.Position.DistanceTo(pawn.Position));
                    return g.Count() > 1 ? $"{g.Key} x{g.Count()} ({dist} tiles)" : $"{g.Key} ({dist} tiles)";
                }));
        }
    }
}
