using System.Collections.Generic;
using RimMind.Core.AgentBus;
using RimMind.Core.Comps;
using Verse;

namespace RimMind.Core.Perception
{
    public static class PerceptionBridge
    {
        public static void PublishPerception(int pawnId, string perceptionType, string content, float importance = 0.5f)
        {
            string npcId = $"NPC-{pawnId}";
            global::RimMind.Core.AgentBus.AgentBus.Publish(new PerceptionEvent(npcId, pawnId, perceptionType, content, importance));
        }

        public static void PublishPerceptionForPawn(Pawn pawn, string perceptionType, string content, float importance = 0.5f)
        {
            if (pawn == null) return;
            if (!CompPawnAgent.IsAgentActive(pawn)) return;
            PublishPerception(pawn.thingIDNumber, perceptionType, content, importance);
        }

        public static void PublishBroadcast(string perceptionType, string content, float importance = 0.5f, Map? map = null)
        {
            var maps = map != null ? new List<Map> { map } : Find.Maps;
            foreach (var m in maps)
            {
                if (m?.mapPawns == null) continue;
                foreach (var pawn in m.mapPawns.FreeColonistsAndPrisoners)
                {
                    if (CompPawnAgent.IsAgentActive(pawn))
                        PublishPerception(pawn.thingIDNumber, perceptionType, content, importance);
                }
            }
        }
    }
}
