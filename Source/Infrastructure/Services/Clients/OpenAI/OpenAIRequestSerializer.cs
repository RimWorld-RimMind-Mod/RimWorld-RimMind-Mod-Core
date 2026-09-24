using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Domain.Llm;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    /// <summary>
    /// Pure (Verse-free, stateless) serializer that turns an <see cref="LlmRequestEnvelope"/>
    /// into the OpenAI chat-completions request body. Extracted from OpenAIClient so the exact
    /// wire request can be unit-tested and snapshotted.
    /// </summary>
    internal static class OpenAIRequestSerializer
    {
        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            string modelName,
            int defaultMaxTokens,
            bool stream = false)
        {
            var body = new OpenAIRequestDto
            {
                model = modelName,
                messages = BuildMessages(envelope),
                max_tokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : defaultMaxTokens,
                temperature = envelope.Temperature,
                stream = stream,
            };

            if (TryParseJson(envelope.JsonSchema, out var parsedSchema))
            {
                body.response_format = new ResponseFormatDto
                {
                    type = "json_schema",
                    json_schema = new { name = "response", schema = parsedSchema },
                };
            }

            if (envelope.Tools != null && envelope.Tools.Count > 0)
            {
                body.tools = new List<ToolDto>();
                foreach (var t in envelope.Tools.OrderBy(t => t.Name, System.StringComparer.Ordinal))
                {
                    body.tools.Add(new ToolDto
                    {
                        Function = new ToolFunctionDto
                        {
                            Name = t.Name,
                            Description = t.Description,
                            Parameters = t.Parameters != null
                                ? JsonConvert.DeserializeObject(t.Parameters)
                                : new { type = "object", properties = new { } },
                        },
                    });
                }
                if (envelope.Tools.Any(t => t.ToolChoice == "required"))
                    body.tool_choice = "required";
                else
                    body.tool_choice = "auto";
            }

            return JsonConvert.SerializeObject(body, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static List<MessageDto> BuildMessages(LlmRequestEnvelope envelope)
        {
            var converted = new List<MessageDto>();
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                foreach (var m in envelope.Messages)
                {
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
                    messages.Add(new MessageDto { role = ex.Role, content = ex.Content });
            }
            for (int i = splitAt; i < converted.Count; i++) messages.Add(converted[i]);
            return messages;
        }

        private static bool TryParseJson(string? value, out object? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value!.TrimStart();
            // OpenAI json_schema must be a JSON object/array. Text sentinels like
            // "<Action>...</Action>" are the Agent text convention, not a JSON schema.
            if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;
            try
            {
                parsed = JsonConvert.DeserializeObject(value);
                return parsed != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
