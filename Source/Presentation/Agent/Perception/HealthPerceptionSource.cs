using System.Collections.Generic;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Common;
using RimMind.Presentation.Agent;
using Verse;

namespace RimMind.Presentation.Agent.Perception
{
    public sealed class HealthPerceptionSource : IPerceptionSource
    {
        public string Id => "rimmind.perception.health";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public int Priority => 20;

        public bool ShouldSense(IAgentInfo agent) => agent.State == Domain.Enums.AgentState.Active;

        public IReadOnlyList<PerceptionBufferEntry> Sense(IAgentInfo agent)
        {
            var entries = new List<PerceptionBufferEntry>();
            if (agent is IPawnAgentVerse pawnAgent)
            {
                var pawn = pawnAgent.Pawn;
                if (pawn?.health?.hediffSet?.hediffs != null)
                {
                    foreach (var hediff in pawn.health.hediffSet.hediffs)
                    {
                        if (hediff != null && hediff.Visible && hediff.Severity > RimMindDefaults.PerceptionMediumThreshold)
                        {
                            entries.Add(new PerceptionBufferEntry
                            {
                                PerceptionType = "health",
                                Content = $"Health issue: {hediff.Label}",
                                Importance = RimMindDefaults.PerceptionHighThreshold,
                                Tick = Find.TickManager.TicksGame
                            });
                        }
                    }
                }
            }
            return entries;
        }
    }
}
