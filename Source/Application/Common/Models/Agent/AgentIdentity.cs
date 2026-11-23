using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Agent
{
    /// <summary>
    /// Agent identity model used by both Core and sub-mods.
    /// Located in Application layer so sub-mods can reference it
    /// via 1_RimMindApplication.dll without depending on the Presentation layer.
    /// IExposable serialization is handled by the Presentation-layer subclass
    /// <see cref="RimMind.Application.Common.Models.Agent.SerializableAgentIdentity"/>.
    /// </summary>
    public class AgentIdentity
    {
        public string NpcId = "";
        public int PawnId;
        public string DisplayName = "";
        public List<string> Motivations = new List<string>();
        public List<string> PersonalityTraits = new List<string>();
        public List<string> CoreValues = new List<string>();

        public AgentIdentity() { }

        public AgentIdentity(string npcId, int pawnId, string displayName)
        {
            NpcId = npcId;
            PawnId = pawnId;
            DisplayName = displayName;
        }

        public override string ToString() => $"AgentIdentity({NpcId}, PawnId={PawnId}, {DisplayName})";
    }
}
