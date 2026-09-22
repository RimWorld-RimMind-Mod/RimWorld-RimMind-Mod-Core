using RimMind.Application.Common.Constants;
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

        public OpenAIClientFactory(IOpenAISettings? openAISettings = null, ILogSink? logSink = null)
        {
            _openAISettings = openAISettings;
            _logSink = logSink;
        }

        public string Id => AIProviders.OpenAI;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string ProviderId => AIProviders.OpenAI;
        public bool RequiresApiKey => true;
        public string DisplayLabel => global::Verse.Translator.Translate("RimMind.Settings.Provider.OpenAI");
        public string? DefaultEndpoint => "https://api.openai.com/v1";
        public string? DefaultModelName => "gpt-4o-mini";
        public int OrderWeight => 10;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var resolvedSettings = settings != null
                ? new OpenAISettingsAdapter(settings)
                : _openAISettings;
            if (resolvedSettings == null) return null;
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
            public bool IsConfigured() => _settings.IsOpenAIConfigured();
        }
    }
}
