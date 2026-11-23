using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Common;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    public class OpenAIClientFactory : IAIClientFactory
    {
        private readonly IOpenAISettings? _openAISettings;
        private readonly ILogSink? _logSink;

        static OpenAIClientFactory()
        {
            HttpTransport.RegisterDecorator(OpenCodeHeaderDecorator.Instance);
            RegisterDefaultPresets();
        }

        public static void RegisterDefaultPresets()
        {
            AIProviderRegistry.RegisterPreset(new AIProviderPreset(
                Id: "openai_official",
                ProviderId: AIProviders.OpenAI,
                Endpoint: "https://api.openai.com/v1",
                ModelName: "gpt-4o-mini",
                DisplayLabelKey: "RimMind.Settings.Provider.OpenAIOfficial",
                FallbackLabel: "OpenAI",
                HostMatchSubstring: "api.openai.com"));
            AIProviderRegistry.RegisterPreset(new AIProviderPreset(
                Id: "openai_deepseek",
                ProviderId: AIProviders.OpenAI,
                Endpoint: "https://api.deepseek.com/v1",
                ModelName: "deepseek-flash",
                DisplayLabelKey: "RimMind.Settings.Provider.DeepSeek",
                FallbackLabel: "DeepSeek",
                HostMatchSubstring: "deepseek.com"));
            AIProviderRegistry.RegisterPreset(new AIProviderPreset(
                Id: "openai_ollama",
                ProviderId: AIProviders.OpenAI,
                Endpoint: "http://localhost:11434/v1",
                ModelName: "llama3.1",
                DisplayLabelKey: null,
                FallbackLabel: "Ollama",
                HostMatchSubstring: ":11434"));
        }

        public OpenAIClientFactory(IOpenAISettings? openAISettings = null, ILogSink? logSink = null)
        {
            _openAISettings = openAISettings;
            _logSink = logSink;
        }

        public string Id => AIProviders.OpenAI;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string ProviderId => AIProviders.OpenAI;
        public bool RequiresApiKey => true;
        public string DisplayLabel => global::Verse.Translator.CanTranslate("RimMind.Settings.Provider.OpenAI")
            ? (string)global::Verse.Translator.Translate("RimMind.Settings.Provider.OpenAI")
            : "OpenAI Compatible";
        public string? DefaultEndpoint => "https://api.openai.com/v1";
        public string? DefaultModelName => "gpt-4o-mini";
        public int OrderWeight => 10;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var resolvedSettings = settings != null
                ? new OpenAISettingsAdapter(settings)
                : _openAISettings;
            if (resolvedSettings == null) return null!;
            return new OpenAIClient(resolvedSettings, _logSink);
        }

        private sealed class OpenAISettingsAdapter : IOpenAISettings
        {
            private readonly ISettingsProvider _settings;

            public OpenAISettingsAdapter(ISettingsProvider settings)
            {
                _settings = settings;
            }

            public string ApiEndpoint => _settings.ApiEndpoint;
            public string ModelName => _settings.ModelName;
            public string ApiKey => _settings.ApiKey;
            public bool ForceJsonMode => _settings.ForceJsonMode;
            public int MaxTokens => _settings.MaxTokens;
            public float DefaultTemperature => _settings.DefaultTemperature;
            public bool DebugLogging => _settings.DebugLogging;
            public System.Collections.Generic.IReadOnlyList<RimMind.Application.Common.Models.Client.BackupEndpointNode>? BackupEndpoints => _settings.BackupEndpoints;
            public bool IsConfigured()
            {
                if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint)
                    && (!string.IsNullOrWhiteSpace(_settings.ApiKey)
                        || RimMind.Application.Features.Llm.RimMindHttpClientPool.IsLoopbackEndpoint(_settings.ApiEndpoint)))
                {
                    return true;
                }

                var backups = _settings.BackupEndpoints;
                if (backups != null)
                {
                    for (int i = 0; i < backups.Count; i++)
                    {
                        if (backups[i]?.IsConfigured == true)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
