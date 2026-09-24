using System.Collections.Generic;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Presentation.Agent;
using Verse;

namespace RimMind.Presentation.Agent.Perception
{
    public sealed class SocialPerceptionSource : IPerceptionSource
    {
        public string Id => "rimmind.perception.social";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public int Priority => 50;

        public bool ShouldSense(IAgentInfo agent) => agent.State == Domain.Enums.AgentState.Active;

        public IReadOnlyList<PerceptionBufferEntry> Sense(IAgentInfo agent)
        {
            var entries = new List<PerceptionBufferEntry>();
            if (agent is IPawnAgentVerse pawnAgent)
            {
                var pawn = pawnAgent.Pawn;
                if (pawn?.relations?.DirectRelations != null)
                {
                    foreach (var rel in pawn.relations.DirectRelations)
                    {
                        if (rel == null) continue;
                        var other = rel.otherPawn;
                        if (other == null || other == pawn || other.Dead) continue;
                        if (other.Position.DistanceTo(pawn.Position) > 10) continue;

                        entries.Add(new PerceptionBufferEntry
                        {
                            PerceptionType = "social",
                            Content = $"Social: {other.Name?.ToStringFull ?? other.Label} ({rel.def.label})",
                            Importance = 0.5f,
                            Tick = Find.TickManager.TicksGame
                        });
                    }
                }
            }
            return entries;
        }
    }
}
