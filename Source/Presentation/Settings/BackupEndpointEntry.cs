using RimMind.Application.Common.Models.Client;
using Verse;

namespace RimMind.Presentation.Settings
{
    /// <summary>
    /// Serializable backup API endpoint configuration stored in RimMindCoreSettings.
    /// API keys are obfuscated when persisted to XML.
    /// </summary>
    public class BackupEndpointEntry : IExposable
    {
        public string name = "OpenCode Go (Backup)";
        public string provider = "opencode";
        public string apiEndpoint = "https://opencode.ai/zen/go/v1";
        public string modelName = "deepseek-v4.1-flash";
        public string apiKey = string.Empty;
        public bool enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "OpenCode Go (Backup)");
            Scribe_Values.Look(ref provider, "provider", "opencode");
            Scribe_Values.Look(ref apiEndpoint, "apiEndpoint", "https://opencode.ai/zen/go/v1");
            Scribe_Values.Look(ref modelName, "modelName", "deepseek-v4.1-flash");
            string storedKey = ApiKeyObfuscator.Obfuscate(apiKey);
            Scribe_Values.Look(ref storedKey, "apiKey", string.Empty);
            apiKey = ApiKeyObfuscator.Deobfuscate(storedKey);
            Scribe_Values.Look(ref enabled, "enabled", true);
        }

        public BackupEndpointNode ToNode() => new BackupEndpointNode
        {
            Name = name ?? string.Empty,
            Provider = provider ?? "openai",
            ApiEndpoint = apiEndpoint ?? string.Empty,
            ModelName = modelName ?? string.Empty,
            ApiKey = apiKey ?? string.Empty,
            Enabled = enabled
        };
    }
}
