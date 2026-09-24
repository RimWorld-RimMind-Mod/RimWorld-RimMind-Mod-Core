using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Verse-specific extensions for IPawnAgentFactory.
    /// Separated to keep IPawnAgentFactory free of framework dependencies.
    /// </summary>
    public interface IPawnAgentFactoryVerse : IPawnAgentFactory
    {
        IPawnAgent Create(Pawn pawn, IAgentBus agentBus);
    }
}
