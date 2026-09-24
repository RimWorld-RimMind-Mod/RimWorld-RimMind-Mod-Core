using RimMind.Application.Common.Interfaces.Agent;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class AgentActiveChecker : IAgentActiveChecker
    {
        public bool IsAgentActive(string pawnThingId)
        {
            if (string.IsNullOrEmpty(pawnThingId)) return false;
            var pawns = PawnsFinder.All_AliveOrDead;
            if (pawns == null) return false;
            foreach (var pawn in pawns)
            {
                if (pawn?.ThingID == pawnThingId)
                    return CompPawnAgent.IsAgentActive(pawn);
            }
            return false;
        }
    }
}
