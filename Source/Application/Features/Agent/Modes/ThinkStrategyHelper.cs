using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Json;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent.Modes;

/// <summary>
/// Shared helper methods for IThinkStrategy implementations.
/// Eliminates code duplication between ReactiveThinkStrategy and ProactiveThinkStrategy.
/// </summary>
public static class ThinkStrategyHelper
{
    public static List<StructuredTool> ConvertToDomainTools(IReadOnlyList<ToolDefinition> defs)
        => defs.Select(d => new StructuredTool
        {
            Name = d.Id,
            Description = d.Description,
            Parameters = d.ParametersSchema,
        }).ToList();

    public static string FormatPerceptions(IReadOnlyList<PerceptionBufferEntry> perceptions)
    {
        if (perceptions.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("<perceptions>");
        foreach (var p in perceptions)
        {
            sb.AppendLine($"[{p.PerceptionType}] {p.Content}" + (p.Importance > 0 ? $" (importance:{p.Importance:F1})" : ""));
        }
        sb.AppendLine("</perceptions>");
        return sb.ToString();
    }

    public static Result<AgentDecision, RimMindError> ParseDecisionCore(
        LlmResponse response,
        IReadOnlyList<ToolResult>? toolCallResults = null)
    {
        var json = JsonTagExtractor.Extract<ActionJson>(response.Content, "Action");
        var hasAction = json != null;
        var hasToolCalls = !string.IsNullOrEmpty(response.ToolCallsJson);

        if (!hasAction && !hasToolCalls)
        {
            // Fallback: AI responded without <Action> tag or ToolCalls — treat as free dialogue
            return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
            {
                ActionIntent = "dialogue.free",
                Reason = response.Content ?? "",
                WantsMoreToolCalls = false,
            });
        }

        if (!hasAction && hasToolCalls)
        {
            // AI issued tool calls but no final action → agentic loop continues
            return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
            {
                WantsMoreToolCalls = true,
            });
        }

        // Action tag present (with or without tool calls) → Action takes priority
        return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
        {
            ActionIntent = json!.action,
            Reason = json.reason ?? "",
            TargetPawnId = json.target,
            Param = json.param,
            WantsMoreToolCalls = false,
        });
    }

    public static List<ChatMessage> BuildDecisionExamples()
    {
        var messages = new List<ChatMessage>();
        foreach (var (user, assistant) in DecisionExampleData.Examples)
        {
            messages.Add(new ChatMessage { Role = "user", Content = user });
            messages.Add(new ChatMessage { Role = "assistant", Content = assistant });
        }
        return messages;
    }

    private class ActionJson
    {
#pragma warning disable CS0649
        public string action = "";
        public string? reason;
        public string? target;
        public string? param;
#pragma warning restore CS0649
    }
}
