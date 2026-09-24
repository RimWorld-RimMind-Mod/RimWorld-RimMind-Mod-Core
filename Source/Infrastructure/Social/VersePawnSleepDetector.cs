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
        foreach (var map in Find.Maps)
        {
            var pawn = map.mapPawns?.AllPawns.FirstOrDefault(p =>
                p.ThingID == npcId || p.thingIDNumber.ToString() == npcId);
            if (pawn != null) return pawn;
        }

        var worldPawn = Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p =>
            p.ThingID == npcId || p.thingIDNumber.ToString() == npcId);
        return worldPawn;
    }
}
