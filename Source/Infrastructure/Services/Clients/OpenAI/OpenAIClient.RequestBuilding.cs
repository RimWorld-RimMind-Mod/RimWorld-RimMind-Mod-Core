using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    public partial class OpenAIClient
    {
        internal static string BuildFormatCacheKey(string? endpoint, string? model) =>
            $"{(endpoint ?? string.Empty).Trim().TrimEnd('/')}|{(model ?? string.Empty).Trim()}";

        private static RimMind.Domain.Llm.LlmRequestEnvelope StripJsonSchema(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            if (string.IsNullOrEmpty(envelope.JsonSchema))
                return envelope;

            return new RimMind.Domain.Llm.LlmRequestEnvelope
            {
                RequestId = envelope.RequestId,
                TraceId = envelope.TraceId,
                ScenarioId = envelope.ScenarioId,
                ModId = envelope.ModId,
                Messages = envelope.Messages,
                SystemAugmentations = envelope.SystemAugmentations,
                JsonSchema = null,
                Tools = envelope.Tools,
                ToolDispatchMode = envelope.ToolDispatchMode,
                Examples = envelope.Examples,
                MaxTokens = envelope.MaxTokens,
                Temperature = envelope.Temperature,
                Priority = envelope.Priority,
                ExpireAtTicks = envelope.ExpireAtTicks,
                MaxRetryCount = envelope.MaxRetryCount,
                IsStreaming = envelope.IsStreaming,
                OnStreamChunk = envelope.OnStreamChunk,
                Ct = envelope.Ct,
                NpcId = envelope.NpcId,
                GameStateInfo = envelope.GameStateInfo,
                Hints = envelope.Hints,
            };
        }

        private string BuildEnvelopeRequestJson(
            RimMind.Domain.Llm.LlmRequestEnvelope envelope,
            bool stream = false,
            string? modelNameOverride = null,
            string? endpointOverride = null,
            string? formatModeOverride = null)
        {
            string effectiveEndpoint = endpointOverride ?? _settings.ApiEndpoint;
            string rawModel = !string.IsNullOrWhiteSpace(modelNameOverride) ? modelNameOverride! : _settings.ModelName;
            string model = NormalizeModelNameForEndpoint(effectiveEndpoint, rawModel);

            if (!_settings.ForceJsonMode)
            {
                return OpenAIRequestSerializer.BuildRequestJson(
                    StripJsonSchema(envelope), model, _settings.MaxTokens, stream, forceJsonMode: false);
            }

            string? formatMode = formatModeOverride;
            if (string.IsNullOrEmpty(formatMode))
            {
                _formatCapabilityCache.TryGetValue(BuildFormatCacheKey(effectiveEndpoint, model), out formatMode);
            }

            if (string.Equals(formatMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                return OpenAIRequestSerializer.BuildRequestJson(
                    StripJsonSchema(envelope), model, _settings.MaxTokens, stream, forceJsonMode: false);
            }

            if (string.Equals(formatMode, "json_object", StringComparison.OrdinalIgnoreCase))
            {
                return OpenAIRequestSerializer.BuildRequestJson(
                    StripJsonSchema(envelope), model, _settings.MaxTokens, stream, forceJsonMode: true);
            }

            return OpenAIRequestSerializer.BuildRequestJson(
                envelope, model, _settings.MaxTokens, stream, forceJsonMode: _settings.ForceJsonMode);
        }

        internal static string NormalizeModelNameForEndpoint(string? endpoint, string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return string.Empty;

            return modelName!.Trim();
        }

        private static void EnsureJsonKeyword(List<MessageDto> messages)
            => OpenAIRequestSerializer.EnsureJsonKeyword(messages);

        private static string FormatEndpoint(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            var uri = new Uri(trimmed);
            string path = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(path))
                return trimmed + "/chat/completions";
            return trimmed + "/v1/chat/completions";
        }
    }
}
