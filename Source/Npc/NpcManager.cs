using System.Collections.Generic;
using System.Linq;
using RimMind.Core.AgentBus;
using RimMind.Core.Settings;
using RimWorld.Planet;
using Verse;

namespace RimMind.Core.Npc
{
    public class NpcManager : GameComponent
    {
        private Dictionary<string, NpcProfile> _registry = new Dictionary<string, NpcProfile>();
        private static NpcManager? _instance;
        public static NpcManager? Instance => _instance;

        public NpcManager(Game game) : base()
        {
            _instance = this;
        }

        public void SpawnNpc(NpcProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.NpcId)) return;
            _registry[profile.NpcId] = profile;
        }

        public void KillNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            if (_registry.TryGetValue(npcId, out var profile))
            {
                AgentBus.AgentBus.Publish(new AgentLifecycleEvent(npcId, 0, "Alive", "Dead"));
                _registry.Remove(npcId);
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

        public static string GetMapNpcId(Map map)
        {
            return $"map-{map.uniqueID}";
        }

        public string GetNpcForMap(Map map)
        {
            string mapNpcId = GetMapNpcId(map);
            if (IsNpcAlive(mapNpcId))
                return mapNpcId;
            return "NPC-storyteller";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _registry, "npcRegistry", LookMode.Value, LookMode.Deep);
            _registry ??= new Dictionary<string, NpcProfile>();
        }
    }
}
