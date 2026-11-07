using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    public partial class OpenAIClient
    {
        private string BuildEnvelopeRequestJson(
            RimMind.Domain.Llm.LlmRequestEnvelope envelope,
            bool stream = false,
            string? modelNameOverride = null,
            string? endpointOverride = null)
        {
            string rawModel = !string.IsNullOrWhiteSpace(modelNameOverride) ? modelNameOverride! : _settings.ModelName;
            string model = NormalizeModelNameForEndpoint(endpointOverride ?? _settings.ApiEndpoint, rawModel);
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
