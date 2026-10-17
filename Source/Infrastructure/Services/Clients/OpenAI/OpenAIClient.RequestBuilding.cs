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
                envelope, model, _settings.MaxTokens, stream);
        }

        internal static string NormalizeModelNameForEndpoint(string? endpoint, string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return string.Empty;

            string trimmedModel = modelName!.Trim();
            if (!string.IsNullOrWhiteSpace(endpoint) &&
                endpoint!.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) >= 0 &&
                trimmedModel.EndsWith("-free", StringComparison.OrdinalIgnoreCase) &&
                trimmedModel.Length > 5)
            {
                return trimmedModel.Substring(0, trimmedModel.Length - 5);
            }

            return trimmedModel;
        }

        private static void EnsureJsonKeyword(List<MessageDto> messages)
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
