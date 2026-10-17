using RimMind.Application.Common.Interfaces.Agent;
using Verse.AI;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Verse-specific extensions for IPawnActor.
    /// Separated to keep IPawnActor free of framework dependencies.
    /// </summary>
    public interface IPawnActorVerse : IPawnActor
    {
        Job? ConsumePendingJob();
        void SetPendingJob(Job job);
        RimMind.Infrastructure.Verse.AgentIntentSession? ActiveIntentSession { get; }
        void StartIntentSession(RimMind.Infrastructure.Verse.AgentIntentSession session);
        void ClearIntentSession();
    }
}
