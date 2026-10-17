using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class AgentActiveChecker : IAgentActiveChecker
    {
        public bool IsAgentActive(string pawnThingId)
        {
            if (string.IsNullOrEmpty(pawnThingId)) return false;
            var pawn = RimMindPawnLookup.FindPawnById(pawnThingId);
            return pawn != null && CompPawnAgent.IsAgentActive(pawn);
        }
    }
}
