using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RimMind.Application.Common.Models.Debug;
using Verse;

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
            string toolCallSummary = BuildToolCallSummary(entry);
            string contentSummary = BuildContentSummary(entry, status);
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

        private static string BuildContentSummary(AIRequestTraceEntry entry, AgentRequestTraceStatus status)
        {
            if (status == AgentRequestTraceStatus.Success)
            {
                if (entry.ToolCalls.Count > 0)
                {
                    if (entry.ToolCalls.Any(t => t.ToolName == "express_dialogue") && !string.IsNullOrWhiteSpace(entry.Response))
                    {
                        if (TryExtractDialogueToolCall(entry.Response, out string dialogueSpeech, out string? dialogueThought)
                            || TryExtractSpeechOrNarration(entry.Response, out dialogueSpeech, out dialogueThought))
                        {
                            string speech = "\"" + ToSingleLine(dialogueSpeech) + "\"";
                            if (!string.IsNullOrWhiteSpace(dialogueThought))
                            {
                                speech += " (" + ToSingleLine(dialogueThought) + ")";
                            }
                            return speech;
                        }
                    }
                    string toolSummary = BuildToolCallSummary(entry);
                    if (!string.IsNullOrWhiteSpace(toolSummary))
                        return toolSummary;
                }

                if (!string.IsNullOrWhiteSpace(entry.Response))
                {
                    if (TryExtractSpeechOrNarration(entry.Response, out string text, out string? thoughtDesc))
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
                return "RimMind.UI.AgentsPage.Trace.WaitingModel".Translate();
            }
            else if (status == AgentRequestTraceStatus.Error)
            {
                string err = ResolveError(entry) ?? "RimMind.UI.AgentsPage.Trace.RequestError".Translate();
                return ToSingleLine(err);
            }

            return FirstNonEmpty(entry.Response, entry.UserPrompt, entry.Source, entry.RequestId);
        }

        private static bool TryExtractSpeechOrNarration(
            string rawResponse,
            out string extractedText,
            out string? thoughtDescription)
        {
            extractedText = string.Empty;
            thoughtDescription = null;

            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            string cleaned = rawResponse.Trim();
            if (cleaned.StartsWith("```"))
            {
                int firstNewline = cleaned.IndexOf('\n');
                if (firstNewline >= 0)
                    cleaned = cleaned.Substring(firstNewline + 1);
                if (cleaned.EndsWith("```"))
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
                // Fall through to regex
            }

            // Regex fallback for reply/narration
            var match = Regex.Match(
                cleaned,
                "\"(?:reply|narration|dialogue|speech|text|content|message)\"\\s*:\\s*\"((?:\\\\\"|[^\"])+)\"",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                extractedText = Regex.Unescape(match.Groups[1].Value);
                return true;
            }

            return false;
        }

        private static bool TryExtractDialogueToolCall(string raw, out string speechText, out string? thoughtText)
        {
            speechText = string.Empty;
            thoughtText = null;

            if (string.IsNullOrWhiteSpace(raw)) return false;

            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(raw.Trim());
                Newtonsoft.Json.Linq.JObject? argsObj = null;

                if (token is Newtonsoft.Json.Linq.JArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item is Newtonsoft.Json.Linq.JObject obj)
                        {
                            string? toolName = (obj["name"] ?? obj["function"]?["name"])?.ToString();
                            if (string.Equals(toolName, "express_dialogue", StringComparison.OrdinalIgnoreCase))
                            {
                                argsObj = ParseArgs(obj);
                                if (argsObj != null) break;
                            }
                        }
                    }
                }
                else if (token is Newtonsoft.Json.Linq.JObject singleObj)
                {
                    argsObj = ParseArgs(singleObj);
                }

                if (argsObj != null)
                {
                    speechText = (argsObj["speech"] ?? argsObj["reply"] ?? argsObj["content"])?.ToString() ?? string.Empty;
                    thoughtText = (argsObj["thought_desc"] ?? argsObj["thought_tag"] ?? argsObj["thought"])?.ToString();

                    if (!string.IsNullOrWhiteSpace(speechText))
                        return true;
                }
            }
            catch
            {
                // Fallback to regex with escaped or unescaped quotes
            }

            var match = Regex.Match(
                raw,
                @"\\?""speech\\?""\s*:\s*\\?""((?:\\.|[^""\\])+)\\?""",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                speechText = Regex.Unescape(match.Groups[1].Value);
                return !string.IsNullOrWhiteSpace(speechText);
            }

            return false;

            static Newtonsoft.Json.Linq.JObject? ParseArgs(Newtonsoft.Json.Linq.JObject obj)
            {
                var argsToken = obj["arguments"] ?? obj["function"]?["arguments"];
                if (argsToken == null) return null;
                if (argsToken.Type == Newtonsoft.Json.Linq.JTokenType.Object && argsToken is Newtonsoft.Json.Linq.JObject jobj) return jobj;
                if (argsToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    string str = (argsToken as Newtonsoft.Json.Linq.JValue)?.Value?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(str)) return Newtonsoft.Json.Linq.JObject.Parse(str);
                }
                return null;
            }
        }

        private static string CleanPromptForSummary(string rawPrompt)
        {
            if (string.IsNullOrWhiteSpace(rawPrompt))
                return "RimMind.UI.AgentsPage.Trace.TriggerInteraction".Translate();

            // Strip [L0] ~ [L9] headers
            string cleaned = Regex.Replace(rawPrompt, @"\[L\d+\]\s*", string.Empty);

            // Check if it's auto trigger template before stripping
            string autoTriggerPrompt = "RimMind.Dialogue.Prompt.AutoTrigger".Translate();
            if ((!string.IsNullOrWhiteSpace(autoTriggerPrompt) && cleaned.Contains(autoTriggerPrompt))
                || rawPrompt.IndexOf("dialogue_trigger", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "RimMind.UI.AgentsPage.Trace.DailyReaction".Translate();
            }

            // Strip XML tags e.g. <dialogue_trigger>...</dialogue_trigger>
            cleaned = Regex.Replace(cleaned, @"<[^>]+>", string.Empty);

            // Collapse newlines and whitespace
            cleaned = ToSingleLine(cleaned);

            if (string.IsNullOrWhiteSpace(cleaned))
                return "RimMind.UI.AgentsPage.Trace.DailyReaction".Translate();

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
                AgentRequestTraceStatus.Success => "RimMind.UI.AgentsPage.Trace.Success".Translate(),
                AgentRequestTraceStatus.Waiting => "RimMind.UI.AgentsPage.Trace.Pending".Translate(),
                AgentRequestTraceStatus.Streaming => "RimMind.UI.AgentsPage.Trace.Pending".Translate(),
                AgentRequestTraceStatus.Error => "RimMind.UI.AgentsPage.Trace.Error".Translate(),
                _ => "RimMind.UI.AgentsPage.Trace.Pending".Translate()
            };

            lines.Add("RimMind.UI.AgentsPage.Trace.TooltipTitle".Translate(statusText));

            var meta = new List<string>();
            if (entry.ElapsedMs > 0) meta.Add("RimMind.UI.AgentsPage.Trace.TooltipElapsed".Translate(entry.ElapsedMs));
            if (entry.TokensUsed > 0) meta.Add("RimMind.UI.AgentsPage.Trace.TooltipTokens".Translate(entry.TokensUsed));
            if (!string.IsNullOrWhiteSpace(entry.Model)) meta.Add("RimMind.UI.AgentsPage.Trace.TooltipModel".Translate(entry.Model));
            if (meta.Count > 0)
            {
                lines.Add(string.Join(" | ", meta));
            }

            if (!string.IsNullOrWhiteSpace(entry.Source))
            {
                lines.Add("RimMind.UI.AgentsPage.Trace.TooltipSource".Translate(entry.Source));
            }

            if (!string.IsNullOrWhiteSpace(entry.UserPrompt))
            {
                string promptClean = CleanPromptForSummary(entry.UserPrompt);
                lines.Add("RimMind.UI.AgentsPage.Trace.TooltipIntent".Translate(promptClean));
            }

            if (entry.ToolCalls.Count > 0)
            {
                string tools = string.Join(", ", entry.ToolCalls.Select(t => t.ToolName));
                lines.Add("RimMind.UI.AgentsPage.Trace.TooltipTools".Translate(tools));
            }

            if (!string.IsNullOrWhiteSpace(contentSummary))
            {
                lines.Add("RimMind.UI.AgentsPage.Trace.TooltipContent".Translate(contentSummary));
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                lines.Add("RimMind.UI.AgentsPage.Trace.TooltipError".Translate(error));
            }

            return string.Join("\n", lines);
        }

        private static string BuildToolCallSummary(AIRequestTraceEntry entry)
        {
            if (entry.ToolCalls.Count == 0)
                return string.Empty;

            var relevantTools = entry.ToolCalls
                .Where(toolCall => toolCall != null
                    && !string.IsNullOrWhiteSpace(toolCall.ToolName)
                    && !string.Equals(toolCall.ToolName, "express_dialogue", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (relevantTools.Count == 0)
                return string.Empty;

            string tools = string.Join(", ",
                relevantTools
                    .Take(ToolCallSummaryLimit)
                    .Select(toolCall => toolCall.ToolName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            return "RimMind.UI.AgentsPage.Trace.CallTools".Translate(tools);
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
            string s = Regex.Replace(text!, @"[\r\n]+", " ");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
                    return ToSingleLine(value);
            }

            return string.Empty;
        }

        private static IReadOnlyList<AgentRequestTraceRow> Empty()
            => new ReadOnlyCollection<AgentRequestTraceRow>(Array.Empty<AgentRequestTraceRow>());
    }
}
