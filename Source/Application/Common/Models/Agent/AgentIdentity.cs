using System.Collections.Generic;

using RimMind.Application.Common.Interfaces.Agent;

namespace RimMind.Application.Common.Models.Agent
{
    /// <summary>
    /// Agent identity model used by both Core and sub-mods.
    /// Located in Application layer so sub-mods can reference it
    /// via 1_RimMindApplication.dll without depending on the Presentation layer.
    /// IExposable serialization is handled by the Presentation-layer subclass
    /// <see cref="RimMind.Application.Common.Models.Agent.SerializableAgentIdentity"/>.
    /// </summary>
    public class AgentIdentity : IAgentIdentity
    {
        public string NpcId = "";
        public int PawnId;
        public string DisplayName = "";
        public List<string> Motivations = new List<string>();
        public List<string> PersonalityTraits = new List<string>();
        public List<string> CoreValues = new List<string>();

        string IAgentIdentity.NpcId => NpcId;
        int IAgentIdentity.PawnId => PawnId;
        string IAgentIdentity.DisplayName => DisplayName;
        List<string> IAgentIdentity.Motivations => Motivations;
        List<string> IAgentIdentity.PersonalityTraits => PersonalityTraits;
        List<string> IAgentIdentity.CoreValues => CoreValues;

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
