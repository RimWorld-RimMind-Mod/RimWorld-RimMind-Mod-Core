using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Engine-free, public wire serializer and response parser for OpenAI-compatible chat-completions endpoints.
    /// Supports an optional <c>customizePayload</c> <see cref="JObject"/> hook so protocol dialect adapters
    /// (e.g. DeepSeek thinking/safety_check) can mutate the payload in a single pass without JSON double-parsing.
    /// </summary>
    public static class OpenAIWireCodec
    {
        private static readonly JsonSerializerSettings WireSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        private static readonly JsonSerializer WireSerializer =
            JsonSerializer.Create(WireSettings);

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            string modelName,
            int defaultMaxTokens,
            bool stream = false,
            bool forceJsonMode = false,
            bool omitZeroMaxTokens = false,
            Action<JObject>? customizePayload = null)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            int effectiveMaxTokens = envelope.MaxTokens > 0
                ? envelope.MaxTokens
                : defaultMaxTokens;

            var messages = BuildMessages(envelope);
            var responseFormat = BuildResponseFormat(envelope, forceJsonMode, messages);
            var (tools, toolChoice) = BuildTools(envelope);

            var payload = new WireRequestDto
            {
                model = modelName,
                messages = messages,
                max_tokens = (omitZeroMaxTokens && effectiveMaxTokens <= 0) ? (int?)null : effectiveMaxTokens,
                temperature = envelope.Temperature,
                stream = stream,
                stream_options = stream ? new WireStreamOptionsDto() : null,
                response_format = responseFormat,
                tools = tools,
                tool_choice = toolChoice,
            };

            if (customizePayload != null)
            {
                var root = JObject.FromObject(payload, WireSerializer);
                customizePayload(root);
                return root.ToString(Formatting.None);
            }

            return JsonConvert.SerializeObject(payload, Formatting.None, WireSettings);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(
            string responseBody,
            LlmRequestEnvelope? envelope = null)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient("Empty response body from model server."));

            try
            {
                var token = JToken.Parse(responseBody.Trim());

                if (token["error"] != null && token["error"]!.Type != JTokenType.Null)
                {
                    string errMsg = AsString(token["error"]?["message"]) ?? "Unknown API error";
                    string errType = AsString(token["error"]?["type"]) ?? string.Empty;
                    string errCode = AsString(token["error"]?["code"]) ?? string.Empty;
                    int embeddedStatus = AsInt(token["error"]?["status"]) ?? 0;
                    string safeErr = LogSecretRedactor.ForLog(errMsg);
                    string combined = $"{errCode} {errType} {errMsg}";

                    if (HttpErrorClassifier.IsContentModerationResponse(400, combined) ||
                        errCode.IndexOf("content_filter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errType.IndexOf("content_filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ContentFiltered(safeErr));
                    }

                    if (HttpErrorClassifier.IsQuotaOrAuthError(embeddedStatus, combined))
                    {
                        return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientQuotaOrAuth(safeErr));
                    }

                    if (HttpErrorClassifier.IsTransientRateLimitOrQueueError(embeddedStatus, combined) ||
                        HttpErrorClassifier.IsTransientHttpFailure(embeddedStatus, combined) ||
                        string.Equals(errType, "server_error", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(errType, "api_error", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(errCode, "server_error", StringComparison.OrdinalIgnoreCase))
                    {
                        return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient(safeErr));
                    }

                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent(safeErr));
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
                string? refusal = AsString(message["refusal"]);

                if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(refusal))
                {
                    string refusalDetail = !string.IsNullOrWhiteSpace(refusal)
                        ? LogSecretRedactor.ForLog(refusal!)
                        : "Response blocked by upstream content filter (finish_reason=content_filter).";
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ContentFiltered(refusalDetail));
                }

                if (string.Equals(finishReason, "insufficient_system_resource", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(finishReason, "aborted", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientTransient($"Upstream generation interrupted (finish_reason={finishReason})."));
                }

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

                if (toolCallsJson == null &&
                    content.Length > 0 &&
                    string.IsNullOrWhiteSpace(content) &&
                    string.IsNullOrWhiteSpace(reasoningContent))
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientTransient("Upstream returned whitespace-only content."));
                }

                var usage = token["usage"];
                int promptTokens = AsInt(usage?["prompt_tokens"]) ?? 0;
                int completionTokens = AsInt(usage?["completion_tokens"]) ?? 0;
                int totalTokens = AsInt(usage?["total_tokens"]) ?? (promptTokens + completionTokens);
                int cachedTokens = AsInt(usage?["prompt_tokens_details"]?["cached_tokens"])
                    ?? AsInt(usage?["prompt_cache_hit_tokens"])
                    ?? 0;

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
                    RimMindErrors.ClientPermanent($"Failed to parse OpenAI response: {LogSecretRedactor.ForLog(ex.Message)}"));
            }
        }

        /// <summary>
        /// Ensures at least one message in a serialized <c>messages</c> JArray contains the keyword
        /// <c>"json"</c>, appending a system instruction if absent (required by OpenAI/DeepSeek <c>json_object</c> mode).
        /// </summary>
        public static void EnsureJsonKeyword(JArray messages)
        {
            if (messages == null) return;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i] is JObject msgObj)
                {
                    string? content = msgObj["content"]?.ToString();
                    if (!string.IsNullOrEmpty(content) &&
                        content!.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }
                }
            }

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i] is JObject msgObj &&
                    string.Equals(msgObj["role"]?.ToString(), "system", StringComparison.OrdinalIgnoreCase))
                {
                    string existing = msgObj["content"]?.ToString() ?? string.Empty;
                    msgObj["content"] = existing + "\n\nPlease respond in JSON format.";
                    return;
                }
            }

            messages.Insert(0, new JObject
            {
                ["role"] = "system",
                ["content"] = "Please respond in JSON format."
            });
        }

        public static bool TryParseJson(string? value, out object? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value!.TrimStart();
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

        private static WireResponseFormatDto? BuildResponseFormat(
            LlmRequestEnvelope envelope,
            bool forceJsonMode,
            List<WireMessageDto> messages)
        {
            if (TryParseJson(envelope.JsonSchema, out var parsedSchema))
            {
                return new WireResponseFormatDto
                {
                    type = "json_schema",
                    json_schema = new { name = "response", schema = parsedSchema },
                };
            }

            if (forceJsonMode)
            {
                EnsureJsonKeyword(messages);
                return new WireResponseFormatDto { type = "json_object" };
            }

            return null;
        }

        private static (List<WireToolDto>? tools, object? toolChoice) BuildTools(LlmRequestEnvelope envelope)
        {
            if (envelope.Tools == null || envelope.Tools.Count == 0)
                return (null, null);

            var tools = new List<WireToolDto>();
            foreach (var t in envelope.Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                tools.Add(new WireToolDto
                {
                    Function = new WireToolFunctionDto
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

        private static void EnsureJsonKeyword(List<WireMessageDto> messages)
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
                messages.Insert(0, new WireMessageDto { role = "system", content = "Please respond in JSON format." });
        }

        private static List<WireMessageDto> BuildMessages(LlmRequestEnvelope envelope)
        {
            var converted = new List<WireMessageDto>();
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                foreach (var m in envelope.Messages)
                {
                    if (m == null) continue;
                    var dto = new WireMessageDto { role = m.Role, content = m.Content };
                    if (m.Role == "assistant" && !string.IsNullOrEmpty(m.ReasoningContent))
                        dto.reasoning_content = m.ReasoningContent;
                    if (!string.IsNullOrEmpty(m.ToolCallId))
                        dto.tool_call_id = m.ToolCallId;
                    if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                    {
                        dto.tool_calls = m.ToolCalls.Select(tc => new WireToolCallDto
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new WireToolCallFunctionDto
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments,
                            },
                        }).ToList();
                    }
                    converted.Add(dto);
                }
            }

            int firstNonSystem = converted.FindIndex(d => d.role != "system");
            int splitAt = firstNonSystem < 0 ? converted.Count : firstNonSystem;

            var messages = new List<WireMessageDto>();
            for (int i = 0; i < splitAt; i++) messages.Add(converted[i]);
            if (envelope.Examples != null && envelope.Examples.Count > 0)
            {
                foreach (var ex in envelope.Examples)
                {
                    if (ex == null) continue;
                    messages.Add(new WireMessageDto { role = ex.Role, content = ex.Content });
                }
            }
            for (int i = splitAt; i < converted.Count; i++) messages.Add(converted[i]);
            return messages;
        }

        private static string? AsString(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return null;
            return token.ToString();
        }

        private static int? AsInt(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return null;
            try { return token.Value<int>(); }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }
        }

#pragma warning disable IDE1006
#pragma warning disable CS0649
        private sealed class WireRequestDto
        {
            public string model = "";
            public List<WireMessageDto>? messages;
            public int? max_tokens;
            public float temperature;
            public bool stream;
            public WireStreamOptionsDto? stream_options;
            public WireResponseFormatDto? response_format;
            public List<WireToolDto>? tools;
            public object? tool_choice;
        }

        private sealed class WireStreamOptionsDto
        {
            [JsonProperty("include_usage")]
            public bool include_usage = true;
        }

        private sealed class WireResponseFormatDto
        {
            public string type = "";
            public object? json_schema;
        }

        private sealed class WireToolDto
        {
            public string type = "function";
            [JsonProperty("function")]
            public WireToolFunctionDto? Function;
        }

        private sealed class WireToolFunctionDto
        {
            [JsonProperty("name")]
            public string Name = "";
            [JsonProperty("description")]
            public string Description = "";
            [JsonProperty("parameters")]
            public object? Parameters;
        }

        private sealed class WireMessageDto
        {
            public string role = "";
            public string? content;
            public string? reasoning_content;
            public string? tool_call_id;
            public List<WireToolCallDto>? tool_calls;
        }

        private sealed class WireToolCallDto
        {
            [JsonProperty("id")]
            public string Id = "";
            [JsonProperty("type")]
            public string Type = "function";
            [JsonProperty("function")]
            public WireToolCallFunctionDto? Function;
        }

        private sealed class WireToolCallFunctionDto
        {
            [JsonProperty("name")]
            public string Name = "";
            [JsonProperty("arguments")]
            public string Arguments = "";
        }
#pragma warning restore CS0649
#pragma warning restore IDE1006
    }
}
