using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent;

/// <summary>
/// Abstraction for resolving agent identity from a Pawn.
/// Decouples Infrastructure patches from Presentation-layer RimMindAPI.Ext.
/// </summary>
public interface IAgentIdentityProvider
{
    AgentIdentity? GetAgentIdentity(object pawn);
}
