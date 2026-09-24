using System.Collections.Generic;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Presentation.Agent;
using Verse;

namespace RimMind.Presentation.Agent.Perception
{
    public sealed class NeedPerceptionSource : IPerceptionSource
    {
        public string Id => "rimmind.perception.need";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public int Priority => 40;

        public bool ShouldSense(IAgentInfo agent) => agent.State == Domain.Enums.AgentState.Active;

        public IReadOnlyList<PerceptionBufferEntry> Sense(IAgentInfo agent)
        {
            var entries = new List<PerceptionBufferEntry>();
            if (agent is IPawnAgentVerse pawnAgent)
            {
                var pawn = pawnAgent.Pawn;
                if (pawn?.needs?.AllNeeds != null)
                {
                    foreach (var need in pawn.needs.AllNeeds)
                    {
                        if (need != null && need.CurLevel < 0.3f)
                        {
                            var importance = (1f - need.CurLevel) * 0.8f;
                            entries.Add(new PerceptionBufferEntry
                            {
                                PerceptionType = "need",
                                Content = $"Need: {need.def.label} at {need.CurLevel:P0}",
                                Importance = importance,
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
