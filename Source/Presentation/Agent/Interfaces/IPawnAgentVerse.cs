using RimMind.Application.Common.Interfaces.Agent;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Verse-specific extensions for IPawnAgent.
    /// Separated to keep IPawnAgent free of framework dependencies.
    /// </summary>
    public interface IPawnAgentVerse : IPawnAgent, IExposable
    {
        Pawn Pawn { get; }
        new Verse.AI.Job? ConsumePendingJob();
        void SetPendingJob(Verse.AI.Job job);
    }
}
