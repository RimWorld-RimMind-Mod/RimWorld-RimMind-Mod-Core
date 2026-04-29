using RimMind.Core.Flywheel;
using Verse;

namespace RimMind.Core.Settings
{
    public class RimMindCoreSettings : ModSettings
    {
        public AIProvider provider = AIProvider.OpenAI;

        public string apiKey = string.Empty;
        public string apiEndpoint = "https://api.deepseek.com/v1";
        public string modelName = "deepseek-chat";
        public string player2RemoteUrl = "https://api.player2.game";

        public bool forceJsonMode = true;

        public int maxTokens = 800;

        public float defaultTemperature = 0.7f;

        public bool debugLogging = false;

        public ContextSettings Context = new ContextSettings();

        public string customPawnPrompt = string.Empty;
        public string customMapPrompt = string.Empty;

        public int contextDiffLifetimeTicks = 600;

        public int contextCalibrateInterval = 10000;

        public bool requestOverlayEnabled = true;
        public float requestOverlayX = 20f;
        public float requestOverlayY = 20f;
        public float requestOverlayW = 300f;
        public float requestOverlayH = 200f;

        public int maxConcurrentRequests = 3;
        public int maxRetryCount = 2;
        public int requestTimeoutMs = 120000;

        public string telemetryDataPath = string.Empty;
        public string embeddingSnapshotPath = string.Empty;
        public string analysisReportPath = string.Empty;
        public FlywheelAutoApplyMode autoApplyMode = FlywheelAutoApplyMode.Off;
        public float autoApplyConfidenceThreshold = 0.8f;

        public bool IsConfigured()
        {
            if (provider == AIProvider.Player2)
                return true;
            return !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiEndpoint);
        }

        public bool IsOpenAIConfigured() =>
            !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiEndpoint);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref provider, "provider", AIProvider.OpenAI);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref apiEndpoint, "apiEndpoint", "https://api.deepseek.com/v1");
            Scribe_Values.Look(ref modelName, "modelName", "deepseek-chat");
            Scribe_Values.Look(ref player2RemoteUrl, "player2RemoteUrl", "https://api.player2.game");
            Scribe_Values.Look(ref forceJsonMode, "forceJsonMode", true);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 800);
            Scribe_Values.Look(ref defaultTemperature, "defaultTemperature", 0.7f);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Deep.Look(ref Context, "Context");
            Context ??= new ContextSettings();
            Scribe_Values.Look(ref customPawnPrompt, "customPawnPrompt", string.Empty);
            Scribe_Values.Look(ref customMapPrompt, "customMapPrompt", string.Empty);
            Scribe_Values.Look(ref contextDiffLifetimeTicks, "contextDiffLifetimeTicks", 600);
            Scribe_Values.Look(ref contextCalibrateInterval, "contextCalibrateInterval", 10000);
            Scribe_Values.Look(ref requestOverlayEnabled, "requestOverlayEnabled", true);
            Scribe_Values.Look(ref requestOverlayX, "requestOverlayX", 20f);
            Scribe_Values.Look(ref requestOverlayY, "requestOverlayY", 20f);
            Scribe_Values.Look(ref requestOverlayW, "requestOverlayW", 300f);
            Scribe_Values.Look(ref requestOverlayH, "requestOverlayH", 200f);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 3);
            Scribe_Values.Look(ref maxRetryCount, "maxRetryCount", 2);
            Scribe_Values.Look(ref requestTimeoutMs, "requestTimeoutMs", 120000);
            Scribe_Values.Look(ref telemetryDataPath, "telemetryDataPath", string.Empty);
            Scribe_Values.Look(ref embeddingSnapshotPath, "embeddingSnapshotPath", string.Empty);
            Scribe_Values.Look(ref analysisReportPath, "analysisReportPath", string.Empty);
            Scribe_Values.Look(ref autoApplyMode, "autoApplyMode", FlywheelAutoApplyMode.Off);
            Scribe_Values.Look(ref autoApplyConfidenceThreshold, "autoApplyConfidenceThreshold", 0.8f);
        }
    }
}
