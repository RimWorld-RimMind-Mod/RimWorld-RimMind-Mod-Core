using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Tools
{
    /// <summary>
    /// A composite ToolCall that orchestrates multiple atomic ToolCalls.
    /// Actions module will implement this interface for high-level intent execution.
    /// </summary>
    public interface ICompositeToolCall : IToolHandler
    {
        /// <summary>
        /// The atomic ToolCall IDs this composite depends on.
        /// </summary>
        IReadOnlyList<string> RequiredToolIds { get; }
    }
}
