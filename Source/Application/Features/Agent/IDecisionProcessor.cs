using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    /// <summary>
    /// Processes AI callback results: validates, handles agentic loop continuation,
    /// executes decisions, records behavior, and publishes events.
    /// Extracted from PawnThinker.ProcessPendingCallback for single-responsibility.
    /// </summary>
    public interface IDecisionProcessor
    {
        /// <summary>
        /// Process the result of an AI think request.
        /// Returns true if the think cycle is complete (decision executed or failed),
        /// false if the agentic loop continues (more tool calls needed).
        /// </summary>
        bool ProcessResult(
            Result<LlmResponse, RimMindError> result,
            LlmRequestContext? context,
            IThinkStrategy strategy,
            IReadOnlyList<ToolDefinition> availableTools,
            int toolCallRound);
    }
}
