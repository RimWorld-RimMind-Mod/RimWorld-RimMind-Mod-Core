using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Extensions
{
    public interface ISensorProvider
    {
        string SensorId { get; }
        int Priority { get; }

        /// <summary>
        /// Poll the sensor for current data for the given pawn.
        /// Returns null or empty if no new data is available.
        /// </summary>
        string? Sense(Pawn pawn);

        /// <summary>
        /// Get Agent Tools that this sensor provides. These are exposed as tool-calling functions for the AI.
        /// Return empty list if no tools.
        /// </summary>
        List<AgentToolDefinition> GetAgentTools(Pawn pawn);

        /// <summary>
        /// Tick interval in game ticks. 0 means only polled on-demand.
        /// </summary>
        int TickInterval { get; }
    }
}
