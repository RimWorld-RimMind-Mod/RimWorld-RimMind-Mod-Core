using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.Shared;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    /// <summary>
    /// Pure (Verse-free, stateless) wire serializer and response parser for OpenAI-compatible
    /// chat-completions endpoints. Shared by Core's built-in client and extension model services.
    /// </summary>
    public static class OpenAIRequestSerializer
    {
        private static readonly JsonSerializerSettings WireSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            string modelName,
            int defaultMaxTokens,
            bool stream = false,
            bool forceJsonMode = false)
            => BuildRequestJson(envelope, modelName, defaultMaxTokens, stream, forceJsonMode, omitZeroMaxTokens: false);

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            string modelName,
            int defaultMaxTokens,
            bool stream,
            bool forceJsonMode,
            bool omitZeroMaxTokens)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            int effectiveMaxTokens = envelope.MaxTokens > 0
                ? (defaultMaxTokens > envelope.MaxTokens && envelope.ScenarioId != "RimMind.Test"
                    ? defaultMaxTokens
                    : envelope.MaxTokens)
                : defaultMaxTokens;

            var messages = BuildMessages(envelope);
            var responseFormat = BuildResponseFormat(envelope, forceJsonMode, messages);
            var (tools, toolChoice) = BuildTools(envelope);

            if (omitZeroMaxTokens && effectiveMaxTokens <= 0)
            {
                var omitBody = new WireRequestDto
                {
                    model = modelName,
                    messages = messages,
                    max_tokens = null,
                    temperature = envelope.Temperature,
                    stream = stream,
                    stream_options = stream ? new StreamOptionsDto() : null,
                    response_format = responseFormat,
                    tools = tools,
                    tool_choice = toolChoice,
                };

                return JsonConvert.SerializeObject(omitBody, Formatting.None, WireSettings);
            }

            var body = new OpenAIRequestDto
            {
                model = modelName,
                messages = messages,
                max_tokens = effectiveMaxTokens,
                temperature = envelope.Temperature,
                stream = stream,
                stream_options = stream ? new StreamOptionsDto() : null,
                response_format = responseFormat,
                tools = tools,
                tool_choice = toolChoice,
            };

            return JsonConvert.SerializeObject(body, Formatting.None, WireSettings);
        }

        /// <summary>
        /// Parses an OpenAI chat-completions JSON response into an <see cref="LlmResponse"/>.
        /// Tool-call names are returned in wire (underscore) form for <c>ToolRegistry</c> to resolve,
        /// <see cref="LlmResponse.ToolCallsJson"/> is null when there are no tool calls, cached
        /// prompt tokens are surfaced, and reasoning-only replies fall back to reasoning text
        /// when the request did not expect tool calls.
        /// </summary>
        public static Result<LlmResponse, RimMindError> ParseResponse(
            string responseBody,
            LlmRequestEnvelope? envelope = null)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient("Empty response body from model server."));

            try
            {
                var token = JToken.Parse(responseBody);

                if (token["error"] != null && token["error"]!.Type != JTokenType.Null)
                {
                    string errMsg = token["error"]?["message"]?.ToString() ?? "Unknown API error";
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientPermanent(LogSanitizer.ForLog(errMsg)));
                }

                var choices = token["choices"] as JArray;
                if (choices == null || choices.Count == 0)
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientPermanent("Response choices array is missing or empty."));
                }

                var message = choices[0]["message"];
                if (message == null || message.Type == JTokenType.Null)
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientPermanent("First choice message is null."));
                }

                string content = AsString(message["content"]) ?? string.Empty;
                string? reasoningContent = AsString(message["reasoning_content"]);
                string? finishReason = AsString(choices[0]["finish_reason"]);

                string? toolCallsJson = null;
                var toolCalls = message["tool_calls"] as JArray;
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    var wireList = new List<object>(toolCalls.Count);
                    foreach (var tc in toolCalls)
                    {
                        string? rawId = AsString(tc["id"]);
                        string id = !string.IsNullOrEmpty(rawId) ? rawId! : Guid.NewGuid().ToString("N");
                        string? rawType = AsString(tc["type"]);
                        string type = !string.IsNullOrEmpty(rawType) ? rawType! : "function";
                        string name = AsString(tc["function"]?["name"]) ?? "";
                        var argsToken = tc["function"]?["arguments"];
                        string arguments = argsToken == null || argsToken.Type == JTokenType.Null
                            ? "{}"
                            : argsToken.Type == JTokenType.String
                                ? argsToken.ToString()
                                : argsToken.ToString(Formatting.None);
                        wireList.Add(new
                        {
                            id = id,
                            type = type,
                            function = new
                            {
                                name = name,
                                arguments = arguments
                            }
                        });
                    }
                    toolCallsJson = JsonConvert.SerializeObject(wireList);
                }

                bool expectsTools = envelope?.Tools != null && envelope.Tools.Count > 0;
                if (!expectsTools &&
                    !string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(content) &&
                    toolCallsJson == null &&
                    !string.IsNullOrWhiteSpace(reasoningContent))
                {
                    content = reasoningContent!;
                }

                var usage = token["usage"];
                int promptTokens = AsInt(usage?["prompt_tokens"]) ?? 0;
                int completionTokens = AsInt(usage?["completion_tokens"]) ?? 0;
                int totalTokens = AsInt(usage?["total_tokens"]) ?? (promptTokens + completionTokens);
                int cachedTokens = AsInt(usage?["prompt_tokens_details"]?["cached_tokens"]) ?? 0;

                var llmResponse = new LlmResponse
                {
                    RequestId = AsString(token["id"]) ?? Guid.NewGuid().ToString("N"),
                    Content = content,
                    ReasoningContent = reasoningContent,
                    ToolCallsJson = toolCallsJson,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    CachedTokens = cachedTokens,
                    TokensUsed = totalTokens
                };

                return Result<LlmResponse, RimMindError>.Ok(llmResponse);
            }
            catch (Exception ex)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"Failed to parse OpenAI response: {LogSanitizer.ForLog(ex.Message)}"));
            }
        }

        internal static ResponseFormatDto? BuildResponseFormat(
            LlmRequestEnvelope envelope,
            bool forceJsonMode,
            List<MessageDto> messages)
        {
            if (TryParseJson(envelope.JsonSchema, out var parsedSchema))
            {
                return new ResponseFormatDto
                {
                    type = "json_schema",
                    json_schema = new { name = "response", schema = parsedSchema },
                };
            }

            if (forceJsonMode)
            {
                EnsureJsonKeyword(messages);
                return new ResponseFormatDto
                {
                    type = "json_object",
                };
            }

            return null;
        }

        internal static (List<ToolDto>? tools, object? toolChoice) BuildTools(LlmRequestEnvelope envelope)
        {
            if (envelope.Tools == null || envelope.Tools.Count == 0)
                return (null, null);

            var tools = new List<ToolDto>();
            foreach (var t in envelope.Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                tools.Add(new ToolDto
                {
                    Function = new ToolFunctionDto
                    {
                        Name = t.Name?.Replace('.', '_') ?? "",
                        Description = t.Description,
                        Parameters = ParseParameters(t.Parameters),
                    },
                });
            }

            object toolChoice = envelope.Tools.Any(t => t.ToolChoice == "required") ? "required" : "auto";
            return (tools, toolChoice);
        }

        private static object? ParseParameters(string? parameters)
        {
            if (parameters == null)
                return new { type = "object", properties = new { } };
            try
            {
                return JsonConvert.DeserializeObject(parameters);
            }
            catch (JsonException)
            {
                return new { type = "object", properties = new { } };
            }
        }

        internal static void EnsureJsonKeyword(List<MessageDto> messages)
        {
            foreach (var m in messages)
            {
                if (m.content != null && m.content.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }
            int lastSys = -1;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].role == "system")
                {
                    lastSys = i;
                    break;
                }
            }
            if (lastSys >= 0)
                messages[lastSys].content = (messages[lastSys].content ?? "") + "\n\nPlease respond in JSON format.";
            else
                messages.Insert(0, new MessageDto { role = "system", content = "Please respond in JSON format." });
        }

        internal static List<MessageDto> BuildMessages(LlmRequestEnvelope envelope, bool mergeConsecutivePlainRoles = false)
        {
            var converted = new List<MessageDto>();
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                foreach (var m in envelope.Messages)
                {
                    if (m == null) continue;
                    var dto = new MessageDto { role = m.Role, content = m.Content };
                    if (m.Role == "assistant" && !string.IsNullOrEmpty(m.ReasoningContent))
                        dto.reasoning_content = m.ReasoningContent;
                    if (!string.IsNullOrEmpty(m.ToolCallId))
                        dto.tool_call_id = m.ToolCallId;
                    if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                    {
                        dto.tool_calls = m.ToolCalls.Select(tc => new ToolCallDto
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallFunctionDto
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments,
                            },
                        }).ToList();
                    }
                    converted.Add(dto);
                }
            }

            // Few-shot examples belong before the live conversation: emit any leading system
            // messages first, then the examples, then the remaining (user/assistant) turns.
            int firstNonSystem = converted.FindIndex(d => d.role != "system");
            int splitAt = firstNonSystem < 0 ? converted.Count : firstNonSystem;

            var messages = new List<MessageDto>();
            for (int i = 0; i < splitAt; i++) messages.Add(converted[i]);
            if (envelope.Examples != null && envelope.Examples.Count > 0)
            {
                foreach (var ex in envelope.Examples)
                {
                    if (ex == null) continue;
                    messages.Add(new MessageDto { role = ex.Role, content = ex.Content });
                }
            }
            for (int i = splitAt; i < converted.Count; i++) messages.Add(converted[i]);
            return mergeConsecutivePlainRoles ? MergeConsecutiveSameRole(messages) : messages;
        }

        internal static List<MessageDto> MergeConsecutiveSameRole(List<MessageDto> messages)
        {
            if (messages == null || messages.Count <= 1) return messages!;

            var merged = new List<MessageDto>(messages.Count);
            var current = messages[0];

            for (int i = 1; i < messages.Count; i++)
            {
                var next = messages[i];
                if (CanMergeConsecutive(current, next))
                {
                    current = new MessageDto
                    {
                        role = current.role,
                        content = (current.content ?? "") + "\n" + (next.content ?? ""),
                        reasoning_content = current.reasoning_content ?? next.reasoning_content,
                    };
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);

            return merged;
        }

        private static bool CanMergeConsecutive(MessageDto a, MessageDto b)
        {
            if (!string.Equals(a.role, b.role, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(a.role, "tool", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(a.tool_call_id) || !string.IsNullOrEmpty(b.tool_call_id)) return false;
            if ((a.tool_calls != null && a.tool_calls.Count > 0) || (b.tool_calls != null && b.tool_calls.Count > 0)) return false;
            return true;
        }

        public static bool TryParseJson(string? value, out object? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value!.TrimStart();
            // OpenAI json_schema must be a JSON object/array. Text sentinels like
            // "<Action>...</Action>" are the Agent text convention, not a JSON schema.
            if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;
            try
            {
                parsed = JsonConvert.DeserializeObject(value!);
                return parsed != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string? AsString(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return null;
            return token.ToString();
        }

        private static int? AsInt(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return null;
            try
            {
                return token.Value<int>();
            }
            catch (FormatException)
            {
                return null;
            }
            catch (InvalidCastException)
            {
                return null;
            }
        }

#pragma warning disable IDE1006
        private sealed class WireRequestDto
        {
            public string model = "";
            public List<MessageDto>? messages;
            public int? max_tokens;
            public float temperature;
            public bool stream;
            public StreamOptionsDto? stream_options;
            public ResponseFormatDto? response_format;
            public List<ToolDto>? tools;
            public object? tool_choice;
        }
#pragma warning restore IDE1006
    }
}
