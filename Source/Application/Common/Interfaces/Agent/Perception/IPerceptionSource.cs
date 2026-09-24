using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Common.Interfaces.Agent.Perception
{
    /// <summary>
    /// Extensible perception source that contributes entries to an agent's perception buffer.
    /// Register via RimMindAPI.Perception.Sources.Register().
    /// </summary>
    public interface IPerceptionSource : IExtension
    {
        /// <summary>Execution priority. Lower values execute first.</summary>
        int Priority { get; }

        /// <summary>Whether this source should sense for the given agent this tick.</summary>
        bool ShouldSense(IAgentInfo agent);

        /// <summary>Produce perception entries for the given agent.</summary>
        IReadOnlyList<PerceptionBufferEntry> Sense(IAgentInfo agent);
    }
}
