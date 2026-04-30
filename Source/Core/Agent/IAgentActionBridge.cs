using System.Collections.Generic;
using RimMind.Core.Client;

namespace RimMind.Core.Agent
{
    public interface IAgentActionBridge
    {
        bool Execute(string intentId, Verse.Pawn actor, Verse.Pawn? target, string? param, string? eventId = null);
        List<StructuredTool> GetAvailableTools(Verse.Pawn pawn);
        RiskLevel? GetRiskLevel(string intentId);
    }
}
