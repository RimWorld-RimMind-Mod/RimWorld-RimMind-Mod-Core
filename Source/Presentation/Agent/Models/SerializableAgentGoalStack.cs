using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using Verse;

namespace RimMind.Application.Common.Models.Agent
{
    /// <summary>
    /// Verse-serializable AgentGoalStack.
    /// Subclass in Presentation layer so Application layer stays Verse-free.
    /// PawnAgent.ExposeData uses Scribe_Deep.Look with this type.
    /// </summary>
    public class SerializableAgentGoalStack : AgentGoalStack, IExposable
    {
        public SerializableAgentGoalStack() { }

        public SerializableAgentGoalStack(IAgentBus agentBus)
            : base(agentBus) { }

        public void ExposeData()
        {
            var goals = _goals as List<AgentGoal>;
            Scribe_Collections.Look(ref goals, "goals", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _goals.Clear();
                if (goals != null) _goals.AddRange(goals);
            }
        }
    }
}
