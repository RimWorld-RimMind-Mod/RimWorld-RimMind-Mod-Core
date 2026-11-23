using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Models.Debug;
using RimMind.Presentation.Api;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public static class AgentRequestTraceRowBuilder
    {
        public const int DefaultLimit = 8;
        private const int ToolCallSummaryLimit = 3;

        private static readonly string[] ReplyFieldNames =
        {
            "reply", "narration", "dialogue", "speech", "text", "content", "message"
        };

        private static readonly Regex ReplyFieldRegex = new Regex(
            "\"(?:reply|narration|dialogue|speech|text|content|message)\"\\s*:\\s*\"((?:\\\\\"|[^\"])+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LayerTagRegex = new Regex(
            @"\[L\d+\]\s*",
            RegexOptions.Compiled);

        private static readonly Regex XmlTagRegex = new Regex(
            @"<[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new Regex(
            @"\s+",
            RegexOptions.Compiled);

        public static IReadOnlyList<AgentRequestTraceRow> BuildRecent(
            IEnumerable<AIRequestTraceEntry>? entries,
            int limit = DefaultLimit)
        {
            if (entries == null || limit <= 0)
                return Empty();

            var rows = entries
                .Where(entry => entry != null)
                .Reverse()
                .Take(limit)
                .Select(BuildRow)
                .ToList();

            return new ReadOnlyCollection<AgentRequestTraceRow>(rows);
        }

        private static AgentRequestTraceRow BuildRow(AIRequestTraceEntry entry)
        {
            var status = MapStatus(entry.State);
            RequestTraceSummary? customSummary = null;
            if (status == AgentRequestTraceStatus.Success)
            {
                RequestTraceSummarizerRegistry.TrySummarizeResponse(entry, out customSummary);
            }

            string toolCallSummary = BuildToolCallSummary(entry, customSummary?.SuppressToolNames);
            string contentSummary = BuildContentSummary(entry, status, customSummary, toolCallSummary);
            string? error = ResolveError(entry);
            string tooltip = BuildTooltipDetail(entry, status, contentSummary, error);

            return new AgentRequestTraceRow(
                status,
                toolCallSummary,
                contentSummary,
                error,
                tooltip);
        }

        private static AgentRequestTraceStatus MapStatus(AIRequestTraceState state)
        {
            switch (state)
            {
                case AIRequestTraceState.Completed:
                    return AgentRequestTraceStatus.Success;
                case AIRequestTraceState.Failed:
                    return AgentRequestTraceStatus.Error;
                case AIRequestTraceState.Running:
                default:
                    return AgentRequestTraceStatus.Waiting;
            }
        }

        private static string BuildContentSummary(
            AIRequestTraceEntry entry,
            AgentRequestTraceStatus status,
            RequestTraceSummary? customSummary,
            string toolCallSummary)
        {
            if (status == AgentRequestTraceStatus.Success)
            {
                if (customSummary != null && !string.IsNullOrWhiteSpace(customSummary.ContentSummary))
                {
                    return ToSingleLine(customSummary.ContentSummary);
                }

                if (!string.IsNullOrWhiteSpace(toolCallSummary))
                {
                    return toolCallSummary;
                }

                if (!string.IsNullOrWhiteSpace(entry.Response))
                {
                    if (TryExtractStructuredReply(entry.Response, out string text, out string? thoughtDesc))
                    {
                        string speech = "\"" + ToSingleLine(text) + "\"";
                        if (!string.IsNullOrWhiteSpace(thoughtDesc))
                        {
                            speech += " (" + ToSingleLine(thoughtDesc) + ")";
                        }
                        return speech;
                    }

                    string plain = ToSingleLine(entry.Response.Trim('\"', ' '));
                    return "\"" + plain + "\"";
                }
            }
            else if (status == AgentRequestTraceStatus.Waiting)
            {
                if (!string.IsNullOrWhiteSpace(entry.UserPrompt))
                {
                    return CleanPromptForSummary(entry.UserPrompt);
                }
                return RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.WaitingModel", "Waiting for model...");
            }
            else if (status == AgentRequestTraceStatus.Error)
            {
                string err = ResolveError(entry) ?? RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.RequestError", "Request error");
                return ToSingleLine(err);
            }

            return FirstNonEmpty(entry.Response, entry.UserPrompt, entry.Source, entry.RequestId);
        }

        private static bool TryExtractStructuredReply(
            string rawResponse,
            out string extractedText,
            out string? thoughtDescription)
        {
            extractedText = string.Empty;
            thoughtDescription = null;

            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            string cleaned = rawResponse.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = cleaned.IndexOf('\n');
                if (firstNewline >= 0)
                    cleaned = cleaned.Substring(firstNewline + 1);
                if (cleaned.EndsWith("```", StringComparison.Ordinal))
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                cleaned = cleaned.Trim();
            }

            int firstBrace = cleaned.IndexOf('{');
            int lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            else
            {
                return false;
            }

            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(cleaned);
                if (dict != null)
                {
                    foreach (var key in ReplyFieldNames)
                    {
                        if (dict.TryGetValue(key, out var val) && val is string str && !string.IsNullOrWhiteSpace(str))
                        {
                            extractedText = str;
                            break;
                        }
                    }

                    if (dict.TryGetValue("thought", out var thoughtObj))
                    {
                        if (thoughtObj is Newtonsoft.Json.Linq.JObject thoughtJObj)
                        {
                            thoughtDescription = thoughtJObj.Value<string>("description")
                                ?? thoughtJObj.Value<string>("tag");
                        }
                        else if (thoughtObj is string tagStr && !string.IsNullOrWhiteSpace(tagStr))
                        {
                            thoughtDescription = tagStr;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(extractedText))
                        return true;
                }
            }
            catch
            {
                // Fall through to regex extraction for truncated JSON payloads.
            }

            var match = ReplyFieldRegex.Match(cleaned);
            if (match.Success)
            {
                extractedText = Regex.Unescape(match.Groups[1].Value);
                return true;
            }

            return false;
        }

        private static string CleanPromptForSummary(string rawPrompt)
        {
            if (string.IsNullOrWhiteSpace(rawPrompt))
                return RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TriggerInteraction", "Triggered interaction");

            if (RequestTraceSummarizerRegistry.TrySummarizePrompt(rawPrompt, out string? customPrompt)
                && !string.IsNullOrWhiteSpace(customPrompt))
            {
                return ToSingleLine(customPrompt);
            }

            string cleaned = LayerTagRegex.Replace(rawPrompt, string.Empty);
            cleaned = XmlTagRegex.Replace(cleaned, string.Empty);
            cleaned = ToSingleLine(cleaned);

            if (string.IsNullOrWhiteSpace(cleaned))
                return RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.DailyReaction", "Daily reaction");

            return cleaned;
        }

        private static string BuildTooltipDetail(
            AIRequestTraceEntry entry,
            AgentRequestTraceStatus status,
            string contentSummary,
            string? error)
        {
            var lines = new List<string>();

            string statusText = status switch
            {
                AgentRequestTraceStatus.Success => RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.Success", "Success"),
                AgentRequestTraceStatus.Waiting => RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.Pending", "Pending"),
                AgentRequestTraceStatus.Streaming => RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.Pending", "Pending"),
                AgentRequestTraceStatus.Error => RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.Error", "Error"),
                _ => RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.Pending", "Pending")
            };

            lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipTitle", "Status: {0}", statusText));

            var meta = new List<string>();
            if (entry.ElapsedMs > 0) meta.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipElapsed", "{0}ms", entry.ElapsedMs));
            if (entry.TokensUsed > 0) meta.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipTokens", "{0} tok", entry.TokensUsed));
            if (!string.IsNullOrWhiteSpace(entry.Model)) meta.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipModel", "Model: {0}", entry.Model));
            if (meta.Count > 0)
            {
                lines.Add(string.Join(" | ", meta));
            }

            if (!string.IsNullOrWhiteSpace(entry.Source))
            {
                lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipSource", "Source: {0}", entry.Source));
            }

            if (!string.IsNullOrWhiteSpace(entry.UserPrompt))
            {
                string promptClean = CleanPromptForSummary(entry.UserPrompt);
                lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipIntent", "Intent: {0}", promptClean));
            }

            if (entry.ToolCalls.Count > 0)
            {
                string tools = string.Join(", ", entry.ToolCalls.Select(t => t.ToolName));
                lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipTools", "Tools: {0}", tools));
            }

            if (!string.IsNullOrWhiteSpace(contentSummary))
            {
                lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipContent", "Content: {0}", contentSummary));
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                lines.Add(RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.TooltipError", "Error: {0}", error!));
            }

            return string.Join("\n", lines);
        }

        private static string BuildToolCallSummary(
            AIRequestTraceEntry entry,
            IReadOnlyList<string>? suppressedToolNames)
        {
            if (entry.ToolCalls.Count == 0)
                return string.Empty;

            var relevantTools = entry.ToolCalls
                .Where(toolCall => toolCall != null
                    && !string.IsNullOrWhiteSpace(toolCall.ToolName)
                    && !IsSuppressedTool(toolCall.ToolName, suppressedToolNames))
                .ToList();

            if (relevantTools.Count == 0)
                return string.Empty;

            string tools = string.Join(", ",
                relevantTools
                    .Take(ToolCallSummaryLimit)
                    .Select(toolCall => toolCall.ToolName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            return RimMindText.TrOr("RimMind.UI.AgentsPage.Trace.CallTools", "Call: {0}", tools);
        }

        private static bool IsSuppressedTool(string toolName, IReadOnlyList<string>? suppressedToolNames)
        {
            if (RequestTraceSummarizerRegistry.ShouldSuppressToolCall(toolName))
                return true;

            if (suppressedToolNames == null || suppressedToolNames.Count == 0)
                return false;

            for (int i = 0; i < suppressedToolNames.Count; i++)
            {
                if (string.Equals(suppressedToolNames[i], toolName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string? ResolveError(AIRequestTraceEntry entry)
        {
            if (entry.State == AIRequestTraceState.Failed && !string.IsNullOrWhiteSpace(entry.Error))
                return entry.Error;

            var failedToolCall = entry.ToolCalls.FirstOrDefault(toolCall =>
                !toolCall.Succeeded && !string.IsNullOrWhiteSpace(toolCall.Error));
            return failedToolCall?.Error;
        }

        private static string ToSingleLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return WhitespaceRegex.Replace(text!, " ").Trim();
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return ToSingleLine(value);
            }

            return string.Empty;
        }

        private static IReadOnlyList<AgentRequestTraceRow> Empty()
            => new ReadOnlyCollection<AgentRequestTraceRow>(Array.Empty<AgentRequestTraceRow>());
    }
}
