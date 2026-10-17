using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Centralized lookup and eligibility policy for Pawns across Core and submodules.
    /// Unifies pawn resolution logic and colonist validity checks.
    /// Unifies pawn resolution logic, thread-safe caching, and colonist validity checks.
    /// </summary>
    public static class RimMindPawnLookup
    {
        private static readonly ConcurrentDictionary<int, Pawn> PawnByNumberCache = new();
        private static readonly ConcurrentDictionary<string, Pawn> PawnByIdCache = new();

        /// <summary>
        /// Explicitly registers or updates a pawn in the thread-safe lookup cache.
        /// Call this on the main thread when pawns are spawned, selected, or enqueued for AI tasks.
        /// </summary>
        public static void CachePawn(Pawn? pawn)
        {
            if (pawn == null) return;
            if (pawn.thingIDNumber > 0)
            {
                PawnByNumberCache[pawn.thingIDNumber] = pawn;
            }
            if (!string.IsNullOrEmpty(pawn.ThingID))
            {
                PawnByIdCache[pawn.ThingID] = pawn;
            }
        }

        /// <summary>
        /// Clears the cached pawn mappings (primarily for testing and game reloads).
        /// </summary>
        public static void ClearCache()
        {
            PawnByNumberCache.Clear();
            PawnByIdCache.Clear();
        }

        /// <summary>
        /// Finds a pawn across world pawns, current map, all maps, and caravans by String ThingID.
        /// Thread-safe: If called off the main thread, only queries cached pawns.
        /// </summary>
        public static Pawn? FindPawnById(string? pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;

            if (PawnByIdCache.TryGetValue(pawnId, out var cached) && cached != null && !cached.DestroyedOrNull())
            {
                return cached;
            }

            // In RimWorld 1.6, accessing Find.Maps or mapPawns off the main thread triggers
            // list pooling errors. When off-thread, only use the thread-safe cache.
            if (!UnityData.IsInMainThread)
            {
                return null;
            }

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    var pawn = map.mapPawns?.AllPawns?
                        .FirstOrDefault(p => p.ThingID == pawnId);
                    if (pawn != null)
                    {
                        CachePawn(pawn);
                        return pawn;
                    }
                }
            }

            if (Find.WorldObjects?.Caravans != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    var pawn = caravan.PawnsListForReading?
                        .FirstOrDefault(p => p.ThingID == pawnId);
                    if (pawn != null)
                    {
                        CachePawn(pawn);
                        return pawn;
                    }
                }
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAliveOrDead?
                .FirstOrDefault(p => p.ThingID == pawnId);
            if (worldPawn != null)
            {
                CachePawn(worldPawn);
                return worldPawn;
            }

            return null;
        }

        /// <summary>
        /// Finds a pawn by numeric thingIDNumber.
        /// Checks current map and all active maps first, then caravans, then world pawns as last resort.
        /// Thread-safe: If called off the main thread, only queries cached pawns.
        /// </summary>
        public static Pawn? FindPawnByNumber(int thingIDNumber)
        {
            if (thingIDNumber <= 0) return null;

            if (PawnByNumberCache.TryGetValue(thingIDNumber, out var cached) && cached != null && !cached.DestroyedOrNull())
            {
                return cached;
            }

            // In RimWorld 1.6, accessing Find.Maps or mapPawns off the main thread triggers
            // list pooling errors. When off-thread, only use the thread-safe cache.
            if (!UnityData.IsInMainThread)
            {
                return null;
            }

            var pawn = Find.CurrentMap?.mapPawns?.FreeColonists?
                .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
            if (pawn != null)
            {
                CachePawn(pawn);
                return pawn;
            }

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    pawn = map.mapPawns?.FreeColonists?
                        .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
                    if (pawn != null)
                    {
                        CachePawn(pawn);
                        return pawn;
                    }

                    pawn = map.mapPawns?.SpawnedColonyAnimals?
                        .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
                    if (pawn != null)
                    {
                        CachePawn(pawn);
                        return pawn;
                    }
                }
            }

            if (Find.WorldObjects?.Caravans != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    pawn = caravan.PawnsListForReading?
                        .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
                    if (pawn != null)
                    {
                        CachePawn(pawn);
                        return pawn;
                    }
                }
            }

            pawn = Find.WorldPawns?.AllPawnsAlive?
                .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
            if (pawn != null)
            {
                CachePawn(pawn);
                return pawn;
            }

            return null;
        }

        /// <summary>
        /// Checks if a pawn is an active spawned player colonist (not dead, not destroyed, spawned on a map, player faction, not prisoner/slave, not kidnapped).
        /// </summary>
        public static bool IsActiveSpawnedColonist(Pawn? pawn)
        {
            if (pawn == null) return false;
            if (pawn.Dead || pawn.Destroyed) return false;
            if (!pawn.Spawned || pawn.Map == null) return false;
            if (!pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;
            if (pawn.IsKidnapped()) return false;
            return true;
        }

        /// <summary>
        /// Checks if a pawn is an eligible active colonist (alive, colonist, conscious, not in mental break).
        /// </summary>
        public static bool IsEligibleColonist(Pawn? pawn)
        {
            if (!IsActiveSpawnedColonist(pawn)) return false;
            if (pawn!.Downed) return false;
            if (pawn.MentalState != null) return false;
            return true;
        }

        /// <summary>
        /// Returns all eligible living colonists on the given map (or current map if not specified).
        /// </summary>
        public static IEnumerable<Pawn> GetEligibleColonists(Map? map = null)
        {
            if (!UnityData.IsInMainThread) yield break;

            var targetMap = map ?? Find.CurrentMap;
            if (targetMap?.mapPawns?.FreeColonistsSpawned == null) yield break;

            foreach (var pawn in targetMap.mapPawns.FreeColonistsSpawned)
            {
                if (IsEligibleColonist(pawn))
                {
                    CachePawn(pawn);
                    yield return pawn;
                }
            }
        }

        /// <summary>
        /// Safely retrieves spawned colonists on the given map without triggering off-main-thread warnings.
        /// If called off the main thread, queries cached active colonists for that map.
        /// </summary>
        public static List<Pawn> GetSpawnedColonistsSafe(Map? map)
        {
            if (map == null) return new List<Pawn>();
            if (UnityData.IsInMainThread && map.mapPawns != null)
            {
                var colonists = map.mapPawns.FreeColonistsSpawned;
                if (colonists != null)
                {
                    foreach (var p in colonists) CachePawn(p);
                    return colonists.ToList();
                }
            }

            return PawnByNumberCache.Values
                .Where(p => p != null && !p.DestroyedOrNull() && p.Map == map && IsActiveSpawnedColonist(p))
                .ToList();
        }

        /// <summary>
        /// Safely retrieves spawned colony prisoners on the given map without triggering off-main-thread warnings.
        /// If called off the main thread, queries cached colony prisoners for that map.
        /// </summary>
        public static List<Pawn> GetSpawnedPrisonersSafe(Map? map)
        {
            if (map == null) return new List<Pawn>();
            if (UnityData.IsInMainThread && map.mapPawns != null)
            {
                var prisoners = map.mapPawns.AllPawnsSpawned?.Where(p => p != null && p.IsPrisoner).ToList();
                if (prisoners != null)
                {
                    foreach (var p in prisoners) CachePawn(p);
                    return prisoners;
                }
            }

            return PawnByNumberCache.Values
                .Where(p => p != null && !p.DestroyedOrNull() && p.Map == map && p.Spawned && p.IsPrisoner)
                .ToList();
        }

        /// <summary>
        /// Checks if a pawn is an active spawned player colony domestic animal or pet (not dead, not destroyed, spawned on a map, player faction, animal).
        /// </summary>
        public static bool IsActiveColonyAnimal(Pawn? pawn)
        {
            if (pawn == null) return false;
            if (pawn.Dead || pawn.Destroyed) return false;
            if (!pawn.Spawned || pawn.Map == null) return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Animal) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;
            return true;
        }

        /// <summary>
        /// Returns all active spawned player colony animals on the given map (or current map if not specified).
        /// </summary>
        public static IEnumerable<Pawn> GetActiveSpawnedColonyAnimals(Map? map = null)
        {
            if (!UnityData.IsInMainThread) yield break;

            var targetMap = map ?? Find.CurrentMap;
            if (targetMap?.mapPawns?.SpawnedColonyAnimals == null) yield break;

            foreach (var pawn in targetMap.mapPawns.SpawnedColonyAnimals)
            {
                if (IsActiveColonyAnimal(pawn))
                {
                    CachePawn(pawn);
                    yield return pawn;
                }
            }
        }
    }
}
