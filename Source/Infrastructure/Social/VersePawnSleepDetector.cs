using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Presentation.Api;
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
        var pawn = RimMindPawnLookup.FindPawnById(npcId);
        if (pawn != null) return pawn;
        if (int.TryParse(npcId, out var num))
        {
            return RimMindPawnLookup.FindPawnByNumber(num);
        }
        return null;
    }
}
