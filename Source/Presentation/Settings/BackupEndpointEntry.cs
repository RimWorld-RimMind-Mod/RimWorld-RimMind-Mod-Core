using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Common;
using Verse;

namespace RimMind.Presentation.Settings
{
    /// <summary>
    /// Serializable backup API endpoint configuration stored in RimMindCoreSettings.
    /// API keys are obfuscated when persisted to XML.
    /// </summary>
    public class BackupEndpointEntry : IExposable
    {
        public string name = "Backup Node";
        public string provider = AIProviders.OpenAI;
        public string apiEndpoint = string.Empty;
        public string modelName = string.Empty;
        public string apiKey = string.Empty;
        public bool enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Backup Node");
            Scribe_Values.Look(ref provider, "provider", AIProviders.OpenAI);
            Scribe_Values.Look(ref apiEndpoint, "apiEndpoint", string.Empty);
            Scribe_Values.Look(ref modelName, "modelName", string.Empty);
            string storedKey = ApiKeyObfuscator.Obfuscate(apiKey);
            Scribe_Values.Look(ref storedKey, "apiKey", string.Empty);
            apiKey = ApiKeyObfuscator.Deobfuscate(storedKey);
            Scribe_Values.Look(ref enabled, "enabled", true);
        }

        public BackupEndpointNode ToNode() => new BackupEndpointNode
        {
            Name = name ?? string.Empty,
            Provider = string.IsNullOrWhiteSpace(provider) ? AIProviders.OpenAI : provider,
            ApiEndpoint = apiEndpoint ?? string.Empty,
            ModelName = modelName ?? string.Empty,
            ApiKey = apiKey ?? string.Empty,
            Enabled = enabled
        };
    }
}
