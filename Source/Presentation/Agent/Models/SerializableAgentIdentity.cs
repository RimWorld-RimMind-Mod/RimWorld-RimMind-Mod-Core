using System.Collections.Generic;
using Verse;

namespace RimMind.Application.Common.Models.Agent
{
    /// <summary>
    /// Verse-serializable AgentIdentity.
    /// Subclass in Presentation layer so Application layer stays Verse-free.
    /// PawnAgent.ExposeData uses Scribe_Deep.Look with this type.
    /// </summary>
    public class SerializableAgentIdentity : AgentIdentity, IExposable
    {
        public SerializableAgentIdentity() { }

        public SerializableAgentIdentity(string npcId, int pawnId, string displayName)
            : base(npcId, pawnId, displayName) { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref NpcId, "npcId", "");
            Scribe_Values.Look(ref PawnId, "pawnId", 0);
            Scribe_Values.Look(ref DisplayName, "displayName", "");
            Scribe_Collections.Look(ref Motivations, "motivations", LookMode.Value);
            Scribe_Collections.Look(ref PersonalityTraits, "personalityTraits", LookMode.Value);
            Scribe_Collections.Look(ref CoreValues, "coreValues", LookMode.Value);
        }
    }
}
