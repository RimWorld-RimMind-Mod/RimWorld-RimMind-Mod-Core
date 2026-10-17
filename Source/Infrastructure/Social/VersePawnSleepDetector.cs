using System.Linq;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Social;

public sealed class VersePawnSleepDetector : ISleepDetector
{
    public bool IsSleeping(IAgentInfo agent)
    {
        var pawn = FindPawn(agent.NpcId);
        if (pawn == null) return false;
        return pawn.CurJobDef == JobDefOf.LayDown
            && pawn.needs?.rest?.CurLevel < 0.3f;
    }

    private static Pawn? FindPawn(string npcId)
    {
        var cached = Presentation.Api.RimMindPawnLookup.FindPawnById(npcId);
        if (cached != null) return cached;
        if (int.TryParse(npcId, out var num))
        {
            cached = Presentation.Api.RimMindPawnLookup.FindPawnByNumber(num);
            if (cached != null) return cached;
        }

        if (UnityData.IsInMainThread)
        {
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    var pawn = map.mapPawns?.AllPawns?.FirstOrDefault(p =>
                        p.ThingID == npcId || p.thingIDNumber.ToString() == npcId);
                    if (pawn != null)
                    {
                        Presentation.Api.RimMindPawnLookup.CachePawn(pawn);
                        return pawn;
                    }
                }
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAlive?.FirstOrDefault(p =>
                p.ThingID == npcId || p.thingIDNumber.ToString() == npcId);
            if (worldPawn != null)
            {
                Presentation.Api.RimMindPawnLookup.CachePawn(worldPawn);
                return worldPawn;
            }
        }

        return null;
    }
}
