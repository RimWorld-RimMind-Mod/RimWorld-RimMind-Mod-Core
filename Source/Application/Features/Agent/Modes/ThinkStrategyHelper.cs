using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    private const int MaxFormattedPerceptions = 25;
    private const int MaxFormattedPerceptionChars = 2800;

    public static string FormatPerceptions(IReadOnlyList<PerceptionBufferEntry> perceptions)
    {
        if (perceptions == null || perceptions.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("<perceptions>");

        // Prioritize the most recent entries and highest importance so the prompt stays well within BudgetOverflowGuardrail (8000 chars)
        int startIndex = System.Math.Max(0, perceptions.Count - MaxFormattedPerceptions);
        int count = 0;
        for (int i = perceptions.Count - 1; i >= startIndex; i--)
        {
            var p = perceptions[i];
            if (p == null) continue;
            string line = $"[{p.PerceptionType}] {p.Content}" + (p.Importance > 0 ? $" (importance:{p.Importance:F1})" : "");
            if (sb.Length + line.Length + 32 > MaxFormattedPerceptionChars)
                break;
            sb.AppendLine(line);
            count++;
        }

        sb.AppendLine("</perceptions>");
        return sb.ToString();
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex PseudoFunctionCallRegex = new Regex(
        @"\b(?<fn>[a-z_][a-z0-9_.]*)\((?<inner>[\s\S]+?)\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex PseudoFunctionKeyRegex = new Regex(
        @"(?:^|,\s*)(?<key>[a-z_][a-z0-9_]*)\)?\s*[:=]\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly List<ChatMessage> CachedDecisionExamples = CreateDecisionExamples();

    public static bool IsQueryToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        return toolName!.StartsWith("query", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("actions_query", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("list_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("inspect_", StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateReason(string? text, int maxChars = 240)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string normalized = text!.Replace("\r\n", " ").Replace('\n', ' ').Trim();
        return normalized.Length <= maxChars ? normalized : normalized.Substring(0, maxChars) + "...";
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
            var (fallbackId, fallbackName, fallbackArgs, fallbackReason) = TryExtractToolCallFromContent(response.Content);
            if (!string.IsNullOrEmpty(fallbackName))
            {
                bool isFallbackQuery = IsQueryToolName(fallbackName);

                return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
                {
                    ActionIntent = fallbackName!,
                    Reason = !string.IsNullOrEmpty(fallbackReason) ? TruncateReason(fallbackReason) : TruncateReason(response.Content),
                    Param = fallbackArgs,
                    ToolCallId = fallbackId,
                    WantsMoreToolCalls = isFallbackQuery,
                });
            }

            // Fallback: AI responded without <Action> tag or ToolCalls — treat as free dialogue
            return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
            {
                ActionIntent = "dialogue.free",
                Reason = TruncateReason(response.Content),
                WantsMoreToolCalls = false,
            });
        }

        if (!hasAction && hasToolCalls)
        {
            var (toolCallId, toolName, argsJson, reasonFromArgs) = TryExtractFirstToolCall(response.ToolCallsJson);
            bool isQueryTool = IsQueryToolName(toolName);

            return Result<AgentDecision, RimMindError>.Ok(new AgentDecision
            {
                ActionIntent = toolName ?? "",
                Reason = !string.IsNullOrEmpty(reasonFromArgs) ? TruncateReason(reasonFromArgs) : TruncateReason(response.Content),
                Param = argsJson,
                ToolCallId = toolCallId,
                WantsMoreToolCalls = isQueryTool,
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

    internal static (string? Id, string? Name, string? ArgsJson, string? Reason) TryExtractToolCallFromContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return (null, null, null, null);
        string trimmed = content!.Trim();

        // 1. Check pseudo-function call syntax: tool_name(key=val, ...)
        // Require a structured tool identifier (containing '_' or '.') immediately followed by '('
        // so ordinary parenthetical prose like "I'm tired (reason: no sleep)" is never treated as a tool call.
        Match match;
        try
        {
            match = PseudoFunctionCallRegex.Match(trimmed);
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, null, null, null);
        }

        if (match.Success)
        {
            string fnName = match.Groups["fn"].Value;
            if (!IsValidFallbackToolIdentifier(fnName))
                return (null, null, null, null);

            string inner = match.Groups["inner"].Value.Trim();
            MatchCollection keyMatches;
            try
            {
                keyMatches = PseudoFunctionKeyRegex.Matches(inner);
            }
            catch (RegexMatchTimeoutException)
            {
                return (null, null, null, null);
            }
            if (keyMatches.Count > 0)
            {
                var jObj = new Newtonsoft.Json.Linq.JObject();
                for (int i = 0; i < keyMatches.Count; i++)
                {
                    string key = keyMatches[i].Groups["key"].Value;
                    int valStart = keyMatches[i].Index + keyMatches[i].Length;
                    int valEnd = (i + 1 < keyMatches.Count) ? keyMatches[i + 1].Index : inner.Length;
                    string rawVal = inner.Substring(valStart, valEnd - valStart).Trim();
                    if ((rawVal.StartsWith("\"") && rawVal.EndsWith("\"") && rawVal.Length >= 2) ||
                        (rawVal.StartsWith("'") && rawVal.EndsWith("'") && rawVal.Length >= 2) ||
                        (rawVal.StartsWith("“") && rawVal.EndsWith("”") && rawVal.Length >= 2) ||
                        (rawVal.StartsWith("(") && rawVal.EndsWith(")") && rawVal.Length >= 2))
                    {
                        rawVal = rawVal.Substring(1, rawVal.Length - 2).Replace("\\\"", "\"");
                    }
                    else if (rawVal.StartsWith("(") && keyMatches[i].Value.IndexOf(')') >= 0)
                    {
                        rawVal = rawVal.Substring(1).TrimEnd(')').Trim();
                    }
                    if (int.TryParse(rawVal, out int intVal))
                        jObj[key] = intVal;
                    else
                        jObj[key] = rawVal;
                }
                string argsJson = jObj.ToString(Newtonsoft.Json.Formatting.None);
                string? reason = jObj.Value<string>("reason");
                return (null, fnName, argsJson, reason);
            }
        }

        return (null, null, null, null);
    }

    private static (string? Id, string? Name, string? ArgsJson, string? Reason) TryExtractFirstToolCall(string? toolCallsJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallsJson)) return (null, null, null, null);
        try
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(toolCallsJson!);
            var first = token is Newtonsoft.Json.Linq.JArray arr && arr.Count > 0
                ? arr[0] as Newtonsoft.Json.Linq.JObject
                : token as Newtonsoft.Json.Linq.JObject;
            if (first == null) return (null, null, null, null);

            string? id = first.Value<string>("id");
            string? name = first.Value<string>("name");
            Newtonsoft.Json.Linq.JToken? argsToken = first["arguments"];

            if (first["function"] is Newtonsoft.Json.Linq.JObject fnObj)
            {
                name ??= fnObj.Value<string>("name");
                argsToken ??= fnObj["arguments"];
            }

            string? argsJson = null;
            string? reason = null;
            if (argsToken != null)
            {
                if (argsToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    argsJson = argsToken.ToString();
                    if (!string.IsNullOrWhiteSpace(argsJson))
                    {
                        try
                        {
                            if (Newtonsoft.Json.Linq.JToken.Parse(argsJson!) is Newtonsoft.Json.Linq.JObject parsedArgs)
                                reason = parsedArgs.Value<string>("reason");
                        }
                        catch { }
                    }
                }
                else if (argsToken is Newtonsoft.Json.Linq.JObject argsObj)
                {
                    argsJson = argsObj.ToString(Newtonsoft.Json.Formatting.None);
                    reason = argsObj.Value<string>("reason");
                }
            }

            return (id, name, argsJson, reason);
        }
        catch
        {
            return (null, null, null, null);
        }
    }

    private static bool IsValidFallbackToolIdentifier(string? fnName)
    {
        if (string.IsNullOrWhiteSpace(fnName)) return false;
        if (fnName!.IndexOf('_') < 0 && fnName.IndexOf('.') < 0) return false;
        if (fnName[0] == '.' || fnName[fnName.Length - 1] == '.') return false;
        foreach (char c in fnName)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
                return false;
        }
        return true;
    }

    public static List<ChatMessage> BuildDecisionExamples() => CachedDecisionExamples;

    private static List<ChatMessage> CreateDecisionExamples()
    {
        var messages = new List<ChatMessage>(DecisionExampleData.Examples.Length * 2);
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
