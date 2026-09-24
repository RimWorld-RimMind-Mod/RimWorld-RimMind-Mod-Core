namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Provides a pending job for Verse's think node system.
    /// Abstracted to Application layer to avoid Infrastructure→Presentation dependency.
    /// </summary>
    public interface IJobProvider
    {
        object? ConsumePendingJob();
    }
}
