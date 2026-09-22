using System.Collections.Concurrent;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Npc;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Events;
using RimMind.Presentation.Runtime.Services;

using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class NpcManager : GameComponent, INpcManager
    {
        private ConcurrentDictionary<string, NpcProfile> _registry = new ConcurrentDictionary<string, NpcProfile>();
        private readonly ConcurrentDictionary<int, Pawn> _pawnIndex = new ConcurrentDictionary<int, Pawn>();
        private readonly HashSet<int> _activeAgentPawnIds = new HashSet<int>();
        private readonly RuntimeServiceRef<IAgentBus> _agentBus = RuntimeServiceRef<IAgentBus>.Optional();
        private static readonly GameServiceRef<INpcManager> CurrentManager = GameServiceRef<INpcManager>.Optional();

        private IAgentBus? GetAgentBus()
            => _agentBus.ValueOrDefault;

        // [Framework-Forced SL] Static accessor for Verse-instantiated GameComponent.
        public static INpcManager? Instance
        {
            get => CurrentManager.ValueOrDefault;
        }

        public NpcManager(Game game) : base() { }

        public override void LoadedGame()
        {
            _pawnIndex.Clear();
        }

        public void RegisterActiveAgent(int thingId)
        {
            lock (_activeAgentPawnIds)
                _activeAgentPawnIds.Add(thingId);
        }

        public void UnregisterActiveAgent(int thingId)
        {
            lock (_activeAgentPawnIds)
                _activeAgentPawnIds.Remove(thingId);
        }

        public HashSet<int> GetActiveAgentPawnIds()
        {
            lock (_activeAgentPawnIds)
                return new HashSet<int>(_activeAgentPawnIds);
        }

        public void IndexPawn(Pawn pawn)
        {
            if (pawn != null)
            {
                _pawnIndex[pawn.thingIDNumber] = pawn;
                RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(pawn);
            }
        }

        void INpcManager.IndexPawn(object pawn) => IndexPawn(pawn as Pawn);

        public void UnindexPawn(int thingId)
        {
            _pawnIndex.TryRemove(thingId, out _);
        }

        internal void ClearPawnIndex()
        {
            _pawnIndex.Clear();
        }

        public void SpawnNpc(NpcProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.NpcId)) return;
            _registry[profile.NpcId] = profile;
        }

        public void KillNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            if (_registry.TryRemove(npcId, out var profile))
            {
                GetAgentBus()?.Publish(new AgentLifecycleEvent(npcId, 0, "Alive", "Dead"));
            }
        }

        public bool IsNpcAlive(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && _registry.ContainsKey(npcId);
        }

        public NpcProfile? GetNpc(string npcId)
        {
            return string.IsNullOrEmpty(npcId) ? null
                : _registry.TryGetValue(npcId, out var p) ? p : null;
        }

        public IReadOnlyList<NpcProfile> GetAllNpcs() => _registry.Values.ToList();

        public string GetMapNpcId(Map map)
        {
            return $"map-{map.uniqueID}";
        }

        string INpcManager.GetMapNpcId(object map) => GetMapNpcId(map as Map ?? throw new System.ArgumentNullException(nameof(map)));

        public string GetNpcForMap(Map map)
        {
            string mapNpcId = GetMapNpcId(map);
            if (IsNpcAlive(mapNpcId))
                return mapNpcId;
            return "NPC-storyteller";
        }

        string INpcManager.GetNpcForMap(object map) => GetNpcForMap(map as Map ?? throw new System.ArgumentNullException(nameof(map)));

        public Pawn? FindPawnByNpcId(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || !npcId.StartsWith("NPC-")) return null;
            if (!int.TryParse(npcId.Substring(4), out int thingId)) return null;

            if (_pawnIndex.TryGetValue(thingId, out var indexed))
            {
                if (!indexed.DestroyedOrNull() && !indexed.Dead)
                    return indexed;
                _pawnIndex.TryRemove(thingId, out _);
            }

            var cached = RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(thingId);
            if (cached != null)
            {
                _pawnIndex[thingId] = cached;
                return cached;
            }

            if (!UnityData.IsInMainThread)
            {
                return null;
            }

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns == null) continue;
                var pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == thingId);
                if (pawn != null)
                {
                    _pawnIndex[thingId] = pawn;
                    RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(pawn);
                    return pawn;
                }
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == thingId);
            if (worldPawn != null)
            {
                _pawnIndex[thingId] = worldPawn;
                RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(worldPawn);
            }
            return worldPawn;
        }

        public Pawn? FindProxyPawnForMap(Map map)
        {
            var colonist = map.mapPawns?.FreeColonists?
                .FirstOrDefault(p => p.IsFreeNonSlaveColonist && !p.Dead);
            if (colonist != null) return colonist;
            return map.mapPawns?.AllPawns?
                .FirstOrDefault(p => p.IsFreeNonSlaveColonist && !p.Dead) ?? null;
        }

        object? INpcManager.FindPawnByNpcId(string npcId) => FindPawnByNpcId(npcId);

        object? INpcManager.FindProxyPawnForMap(object map) => FindProxyPawnForMap(map as Map ?? throw new System.ArgumentNullException(nameof(map)));

        public override void ExposeData()
        {
            base.ExposeData();
            var dict = new Dictionary<string, NpcProfile>(_registry);
            Scribe_Collections.Look(ref dict, "npcRegistry", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _registry.Clear();
                if (dict != null)
                    foreach (var kv in dict)
                        _registry[kv.Key] = kv.Value;
            }
            _registry ??= new ConcurrentDictionary<string, NpcProfile>();
        }
    }
}
