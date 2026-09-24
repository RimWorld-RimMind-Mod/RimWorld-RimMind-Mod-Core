using System.Collections.Generic;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Modes;

/// <summary>
/// Strategy for building LLM request envelopes and parsing AI responses into agent decisions.
///
/// Implicit contract:
/// <list type="bullet">
///   <item><c>ScenarioId</c> must match the scenario ID used in telemetry and context building.</item>
///   <item><c>BuildEnvelope</c> must include the agent's current mode ID in the envelope so
///   downstream middleware (e.g., TelemetryMiddleware) can tag it.</item>
///   <item><c>ParseDecision</c> must return <c>Result.IsOk = false</c> for unparseable responses;
///   PawnThinker will publish a DecisionFailedEvent on failure.</item>
///   <item><c>ParseDecision</c> should set <c>AgentDecision.WantsMoreToolCalls = true</c> when
///   the AI requests additional tool calls, enabling the agentic loop in PawnThinker.</item>
///   <item>Implementations must be stateless or thread-safe; the same instance is shared
///   across all agents using the same mode.</item>
/// </list>
/// </summary>
public interface IThinkStrategy
{
    string ScenarioId { get; }

    LlmRequestEnvelope BuildEnvelope(
        IAgentInfo agent,
        IReadOnlyList<PerceptionBufferEntry> perceptions,
        IReadOnlyList<ToolDefinition> availableTools);

    Result<AgentDecision, RimMindError> ParseDecision(
        IAgentInfo agent,
        LlmResponse response,
        IReadOnlyList<ToolResult>? toolCallResults = null);
}
