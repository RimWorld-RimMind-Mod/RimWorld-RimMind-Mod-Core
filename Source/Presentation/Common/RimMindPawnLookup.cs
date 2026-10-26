using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Helpers;
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
        /// How long a main-thread lookup miss suppresses rescans of the same id (~250 ticks at 1x speed).
        /// Real time is used so misses also expire while paused and across save reloads.
        /// </summary>
        internal static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(4);

        private static readonly ExpiringNegativeCache<int> MissByNumber = new(MissTtl);
        private static readonly ExpiringNegativeCache<string> MissById = new(MissTtl);

        /// <summary>
        /// Explicitly registers or updates a pawn in the thread-safe lookup cache.
        /// Call this on the main thread when pawns are spawned, selected, or enqueued for AI tasks.
        /// </summary>
        public static void CachePawn(Pawn? pawn)
        {
            if (pawn == null) return;
            if (pawn.Dead || pawn.DestroyedOrNull())
            {
                UncachePawn(pawn);
                return;
            }

            int number = pawn.thingIDNumber;
            // Hot path: already registered as this exact instance. Skips both dictionary writes
            // and the ThingID string allocation (ThingID concatenates on every access in RimWorld).
            if (number > 0
                && PawnByNumberCache.TryGetValue(number, out var existing)
                && ReferenceEquals(existing, pawn))
            {
                return;
            }

            if (number > 0)
            {
                PawnByNumberCache[number] = pawn;
                MissByNumber.Forget(number);
            }
            string thingId = pawn.ThingID;
            if (!string.IsNullOrEmpty(thingId))
            {
                if (!PawnByIdCache.TryGetValue(thingId, out var existingById) || !ReferenceEquals(existingById, pawn))
                {
                    PawnByIdCache[thingId] = pawn;
                }
                MissById.Forget(thingId);
            }
        }

        /// <summary>
        /// Explicitly unregisters a pawn from the thread-safe lookup cache by numeric ID.
        /// </summary>
        public static void UncachePawn(int thingIDNumber)
        {
            if (thingIDNumber <= 0) return;
            if (PawnByNumberCache.TryRemove(thingIDNumber, out var pawn))
            {
                if (pawn != null && !string.IsNullOrEmpty(pawn.ThingID))
                {
                    PawnByIdCache.TryRemove(pawn.ThingID, out _);
                }
            }
            else
            {
                foreach (var kvp in PawnByIdCache)
                {
                    if (kvp.Value?.thingIDNumber == thingIDNumber)
                    {
                        PawnByIdCache.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Explicitly unregisters a pawn from the thread-safe lookup cache by string ThingID.
        /// </summary>
        public static void UncachePawn(string? thingId)
        {
            if (string.IsNullOrEmpty(thingId)) return;
            if (PawnByIdCache.TryRemove(thingId, out var pawn))
            {
                if (pawn != null && pawn.thingIDNumber > 0)
                {
                    PawnByNumberCache.TryRemove(pawn.thingIDNumber, out _);
                }
            }
            else
            {
                foreach (var kvp in PawnByNumberCache)
                {
                    if (kvp.Value?.ThingID == thingId)
                    {
                        PawnByNumberCache.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Explicitly unregisters a pawn instance from the thread-safe lookup cache.
        /// </summary>
        public static void UncachePawn(Pawn? pawn)
        {
            if (pawn == null) return;
            if (pawn.thingIDNumber > 0)
            {
                PawnByNumberCache.TryRemove(pawn.thingIDNumber, out _);
            }
            if (!string.IsNullOrEmpty(pawn.ThingID))
            {
                PawnByIdCache.TryRemove(pawn.ThingID, out _);
            }
        }

        /// <summary>
        /// Clears the cached pawn mappings (primarily for testing and game reloads).
        /// </summary>
        public static void ClearCache()
        {
            PawnByNumberCache.Clear();
            PawnByIdCache.Clear();
            MissByNumber.Clear();
            MissById.Clear();
        }

        /// <summary>
        /// Finds a live pawn by String ThingID across all loaded maps (spawned and unspawned map pawns,
        /// including prisoners and visitors) and player caravans. World pawns are never scanned.
        /// Repeated misses are remembered for <see cref="MissTtl"/> to avoid rescanning.
        /// Thread-safe: If called off the main thread, only queries cached pawns.
        /// </summary>
        public static Pawn? FindPawnById(string? pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;

            if (PawnByIdCache.TryGetValue(pawnId, out var cached))
            {
                if (cached != null && !cached.Dead && !cached.DestroyedOrNull())
                {
                    return cached;
                }
                UncachePawn(cached?.thingIDNumber ?? 0);
                PawnByIdCache.TryRemove(pawnId, out _);
            }

            // In RimWorld 1.6, accessing Find.Maps or mapPawns off the main thread triggers
            // list pooling errors. When off-thread, only use the thread-safe cache.
            if (!UnityData.IsInMainThread)
            {
                return null;
            }

            if (MissById.IsRecentMiss(pawnId)) return null;

            // ThingID is "<defName><thingIDNumber>"; compare the cheap number first so the scan
            // does not allocate a ThingID string for every pawn.
            int numberHint = TryParseTrailingNumber(pawnId);
            var pawn = ScanLoadedPawns(p =>
                (numberHint <= 0 || p.thingIDNumber == numberHint) && p.ThingID == pawnId);
            if (pawn != null)
            {
                CachePawn(pawn);
                return pawn;
            }

            MissById.RecordMiss(pawnId);
            return null;
        }

        /// <summary>
        /// Finds a live pawn by numeric thingIDNumber across all loaded maps (spawned and unspawned
        /// map pawns, including prisoners, visitors and colony animals) and player caravans.
        /// World pawns are never scanned. Repeated misses are remembered for <see cref="MissTtl"/>.
        /// Thread-safe: If called off the main thread, only queries cached pawns.
        /// </summary>
        public static Pawn? FindPawnByNumber(int thingIDNumber)
        {
            if (thingIDNumber <= 0) return null;

            if (PawnByNumberCache.TryGetValue(thingIDNumber, out var cached))
            {
                if (cached != null && !cached.Dead && !cached.DestroyedOrNull())
                {
                    return cached;
                }
                UncachePawn(thingIDNumber);
            }

            // In RimWorld 1.6, accessing Find.Maps or mapPawns off the main thread triggers
            // list pooling errors. When off-thread, only use the thread-safe cache.
            if (!UnityData.IsInMainThread)
            {
                return null;
            }

            if (MissByNumber.IsRecentMiss(thingIDNumber)) return null;

            var pawn = ScanLoadedPawns(p => p.thingIDNumber == thingIDNumber);
            if (pawn != null)
            {
                CachePawn(pawn);
                return pawn;
            }

            MissByNumber.RecordMiss(thingIDNumber);
            return null;
        }

        /// <summary>
        /// Main-thread only. Visits each map's AllPawns exactly once (the getter rebuilds a list per call),
        /// then player caravans. Dead or destroyed pawns never match.
        /// </summary>
        private static Pawn? ScanLoadedPawns(Func<Pawn, bool> match)
        {
            var currentMap = Find.CurrentMap;
            if (currentMap != null)
            {
                var found = ScanMapPawns(currentMap, match);
                if (found != null) return found;
            }

            var maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    if (ReferenceEquals(maps[i], currentMap)) continue;
                    var found = ScanMapPawns(maps[i], match);
                    if (found != null) return found;
                }
            }

            var caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                for (int i = 0; i < caravans.Count; i++)
                {
                    var members = caravans[i]?.PawnsListForReading;
                    if (members == null) continue;
                    for (int j = 0; j < members.Count; j++)
                    {
                        var p = members[j];
                        if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                    }
                }
            }

            return null;
        }

        private static Pawn? ScanMapPawns(Map? map, Func<Pawn, bool> match)
        {
            var mp = map?.mapPawns;
            if (mp == null) return null;

            var allPawns = mp.AllPawns;
            if (allPawns != null)
            {
                for (int j = 0; j < allPawns.Count; j++)
                {
                    var p = allPawns[j];
                    if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                }
            }

            var freeColonists = mp.FreeColonists;
            if (freeColonists != null)
            {
                for (int j = 0; j < freeColonists.Count; j++)
                {
                    var p = freeColonists[j];
                    if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                }
            }

            var freeColonistsSpawned = mp.FreeColonistsSpawned;
            if (freeColonistsSpawned != null)
            {
                for (int j = 0; j < freeColonistsSpawned.Count; j++)
                {
                    var p = freeColonistsSpawned[j];
                    if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                }
            }

            var allPawnsSpawned = mp.AllPawnsSpawned;
            if (allPawnsSpawned != null)
            {
                for (int j = 0; j < allPawnsSpawned.Count; j++)
                {
                    var p = allPawnsSpawned[j];
                    if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                }
            }

            var animals = mp.SpawnedColonyAnimals;
            if (animals != null)
            {
                for (int j = 0; j < animals.Count; j++)
                {
                    var p = animals[j];
                    if (p != null && !p.Dead && !p.DestroyedOrNull() && match(p)) return p;
                }
            }

            return null;
        }

        private static int TryParseTrailingNumber(string id)
        {
            int end = id.Length;
            int start = end;
            while (start > 0 && char.IsDigit(id[start - 1])) start--;
            if (start == end || end - start > 9) return 0;
            return int.TryParse(id.Substring(start, end - start), out int n) ? n : 0;
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
        /// Materializes a snapshot list so callers can safely invoke nested mapPawns queries during iteration.
        /// </summary>
        public static IEnumerable<Pawn> GetEligibleColonists(Map? map = null)
        {
            var result = new List<Pawn>();
            if (!UnityData.IsInMainThread) return result;

            var targetMap = map ?? Find.CurrentMap;
            var spawned = targetMap?.mapPawns?.FreeColonistsSpawned;
            if (spawned == null) return result;

            for (int i = 0; i < spawned.Count; i++)
            {
                var pawn = spawned[i];
                if (IsEligibleColonist(pawn))
                {
                    CachePawn(pawn);
                    result.Add(pawn);
                }
            }
            return result;
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
                    var snapshot = new List<Pawn>(colonists.Count);
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        var p = colonists[i];
                        CachePawn(p);
                        snapshot.Add(p);
                    }
                    return snapshot;
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
                var allSpawned = map.mapPawns.AllPawnsSpawned;
                if (allSpawned != null)
                {
                    var prisoners = new List<Pawn>();
                    for (int i = 0; i < allSpawned.Count; i++)
                    {
                        var p = allSpawned[i];
                        if (p != null && !p.Dead && !p.Destroyed && p.IsPrisoner)
                        {
                            CachePawn(p);
                            prisoners.Add(p);
                        }
                    }
                    return prisoners;
                }
            }

            return PawnByNumberCache.Values
                .Where(p => p != null && !p.Dead && !p.Destroyed && !p.DestroyedOrNull() && p.Map == map && p.Spawned && p.IsPrisoner)
                .ToList();
        }

        /// <summary>
        /// Safely retrieves all free colonists (spawned or unspawned) on the given map as a materialized snapshot list
        /// without allocating a List&lt;Pawn&gt;.Enumerator over MapPawns' reusable internal buffer.
        /// </summary>
        public static List<Pawn> GetFreeColonistsSafe(Map? map)
        {
            var snapshot = new List<Pawn>();
            if (map == null || !UnityData.IsInMainThread || map.mapPawns == null)
                return snapshot;

            var colonists = map.mapPawns.FreeColonists;
            if (colonists == null)
                return snapshot;

            for (int i = 0; i < colonists.Count; i++)
            {
                var p = colonists[i];
                if (p != null && !p.Dead && !p.DestroyedOrNull())
                {
                    CachePawn(p);
                    snapshot.Add(p);
                }
            }
            return snapshot;
        }

        /// <summary>
        /// Formats a raw tool/action identifier (e.g. "actions_place_blueprint", "pawn.job.take_job")
        /// into a clean human-readable title ("Place Blueprint", "Take Job") for UI overlays, motes, and approval cards.
        /// </summary>
        public static string FormatHumanReadableActionName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "Action";

            var trimmed = rawName!.Trim();
            string[] prefixes = { "actions_", "pawn_", "rimmind_", "pawn.job.", "pawn.", "map." };
            foreach (var prefix in prefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Length > prefix.Length)
                {
                    trimmed = trimmed.Substring(prefix.Length);
                    break;
                }
            }

            var tokens = trimmed.Split(new[] { '_', '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return trimmed;

            var formatted = new string[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                formatted[i] = token.Length == 1
                    ? token.ToUpperInvariant()
                    : char.ToUpperInvariant(token[0]) + token.Substring(1);
            }

            return string.Join(" ", formatted);
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
        /// Materializes a snapshot list so callers can safely invoke nested mapPawns queries during iteration.
        /// </summary>
        public static IEnumerable<Pawn> GetActiveSpawnedColonyAnimals(Map? map = null)
        {
            var result = new List<Pawn>();
            if (!UnityData.IsInMainThread) return result;

            var targetMap = map ?? Find.CurrentMap;
            var animals = targetMap?.mapPawns?.SpawnedColonyAnimals;
            if (animals == null) return result;

            for (int i = 0; i < animals.Count; i++)
            {
                var pawn = animals[i];
                if (IsActiveColonyAnimal(pawn))
                {
                    CachePawn(pawn);
                    result.Add(pawn);
                }
            }
            return result;
        }
    }
}
