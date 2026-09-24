using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

public interface IInformationDiffuser
{
    bool ShouldDiffuse(IAgentInfo source, IAgentInfo target, RumorEntry rumor);
    Result<RumorEntry, RimMindError> Diffuse(IAgentInfo source, IAgentInfo target, RumorEntry original);
    IReadOnlyList<RumorEntry> GetKnownRumors(string npcId);
    void AddRumor(string npcId, RumorEntry rumor);
}
