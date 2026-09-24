using System;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Models;
using Verse;

namespace RimMind.Presentation.Settings
{
    public class RimMindCoreSettings : ModSettings, IOpenAISettings
    {
        public const string DefaultApiEndpoint = "https://api.deepseek.com/v1";
        public const string DefaultModelName = "deepseek-v4-flash";

        public string? SavedModVersion;

        public string provider = AIProviderRegistry.GetDefaultProviderId();

        public string apiKey = string.Empty;
        public string apiEndpoint = DefaultApiEndpoint;
        public string modelName = DefaultModelName;
        public string player2RemoteUrl = "https://api.player2.game";

        public bool forceJsonMode = true;

        public int maxTokens = RimMindDefaults.MaxTokens;

        public float defaultTemperature = RimMindDefaults.DefaultTemperature;

        public bool debugLogging = false;

        public ContextSettings Context = new ContextSettings();

        public string customPawnPrompt = string.Empty;
        public string customMapPrompt = string.Empty;

        public int contextDiffLifetimeTicks = RimMindDefaults.ContextDiffLifetimeTicks;

        public int contextCalibrateInterval = RimMindDefaults.FlywheelCalibrateInterval;

        public bool requestOverlayEnabled = true;
        public float requestOverlayX = 20f;
        public float requestOverlayY = 20f;
        public float requestOverlayW = 300f;
        public float requestOverlayH = 200f;
        public bool showAgentProgressFloat = false;
        public bool requestOverlayAutoHideWhenEmpty = true;
        public bool enableFloatingMentalMonitor = false;

        public int maxConcurrentRequests = 3;
        public int maxRetryCount = 2;
        public int requestTimeoutMs = RimMindDefaults.DefaultRequestTimeoutMs;

        public string telemetryDataPath = string.Empty;
        public string embeddingSnapshotPath = string.Empty;
        public string analysisReportPath = string.Empty;
        public FlywheelAutoApplyMode autoApplyMode = FlywheelAutoApplyMode.Off;
        public float autoApplyConfidenceThreshold = RimMindDefaults.AutoApplyConfidenceThreshold;

        public int thinkCooldownTicks = RimMindDefaults.ThinkCooldownTicks;
        public int agentTickInterval = RimMindDefaults.AgentTickInterval;
        public int maxToolCallDepth = RimMindDefaults.DefaultMaxToolCallDepth;
        public int requestExpireTicks = RimMindDefaults.RequestExpireTicks;
        public int behaviorHistoryMax = RimMindDefaults.BehaviorHistoryMax;
        public int queueProcessInterval = RimMindDefaults.QueueProcessInterval;
        public int defaultModCooldownTicks = RimMindDefaults.DefaultModCooldownTicks;
        public float activityFrequencyScale = RimMindDefaults.DefaultActivityFrequencyScale;
        public bool autoActivateColonistAgents = false;

        public AgentAutonomyLevel autonomyLevel = AgentAutonomyLevel.Guided;

        public int circuitBreakerFailureThreshold = RimMindDefaults.CircuitBreakerFailureThreshold;
        public int circuitBreakerOpenDurationSec = RimMindDefaults.CircuitBreakerOpenDurationSec;

        string IOpenAISettings.ApiEndpoint => apiEndpoint;
        string IOpenAISettings.ModelName => modelName;
        string IOpenAISettings.ApiKey => apiKey;
        bool IOpenAISettings.ForceJsonMode => forceJsonMode;
        int IOpenAISettings.MaxTokens => maxTokens;
        float IOpenAISettings.DefaultTemperature => defaultTemperature;
        bool IOpenAISettings.DebugLogging => debugLogging;

        public bool IsConfigured()
        {
            if (!AIProviderRegistry.RequiresApiKey(provider))
                return true;
            return !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiEndpoint);
        }

        public bool IsOpenAIConfigured() =>
            !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiEndpoint);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref SavedModVersion, "savedModVersion");
            Scribe_Values.Look(ref provider, "provider", AIProviderRegistry.GetDefaultProviderId());
            string storedKey = ApiKeyObfuscator.Obfuscate(apiKey);
            Scribe_Values.Look(ref storedKey, "apiKey", string.Empty);
            apiKey = ApiKeyObfuscator.Deobfuscate(storedKey);
            Scribe_Values.Look(ref apiEndpoint, "apiEndpoint", DefaultApiEndpoint);
            Scribe_Values.Look(ref modelName, "modelName", DefaultModelName);
            Scribe_Values.Look(ref player2RemoteUrl, "player2RemoteUrl", "https://api.player2.game");
            Scribe_Values.Look(ref forceJsonMode, "forceJsonMode", true);
            Scribe_Values.Look(ref maxTokens, "maxTokens", RimMindDefaults.MaxTokens);
            Scribe_Values.Look(ref defaultTemperature, "defaultTemperature", RimMindDefaults.DefaultTemperature);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Deep.Look(ref Context, "Context");
            Context ??= new ContextSettings();
            Scribe_Values.Look(ref customPawnPrompt, "customPawnPrompt", string.Empty);
            Scribe_Values.Look(ref customMapPrompt, "customMapPrompt", string.Empty);
            Scribe_Values.Look(ref contextDiffLifetimeTicks, "contextDiffLifetimeTicks", RimMindDefaults.ContextDiffLifetimeTicks);
            Scribe_Values.Look(ref contextCalibrateInterval, "contextCalibrateInterval", RimMindDefaults.FlywheelCalibrateInterval);
            Scribe_Values.Look(ref requestOverlayEnabled, "requestOverlayEnabled", true);
            Scribe_Values.Look(ref requestOverlayX, "requestOverlayX", 20f);
            Scribe_Values.Look(ref requestOverlayY, "requestOverlayY", 20f);
            Scribe_Values.Look(ref requestOverlayW, "requestOverlayW", 300f);
            Scribe_Values.Look(ref requestOverlayH, "requestOverlayH", 200f);
            Scribe_Values.Look(ref showAgentProgressFloat, "showAgentProgressFloat", false);
            Scribe_Values.Look(ref requestOverlayAutoHideWhenEmpty, "requestOverlayAutoHideWhenEmpty", true);
            Scribe_Values.Look(ref enableFloatingMentalMonitor, "enableFloatingMentalMonitor", false);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 3);
            Scribe_Values.Look(ref maxRetryCount, "maxRetryCount", 2);
            Scribe_Values.Look(ref requestTimeoutMs, "requestTimeoutMs", RimMindDefaults.DefaultRequestTimeoutMs);
            Scribe_Values.Look(ref telemetryDataPath, "telemetryDataPath", string.Empty);
            Scribe_Values.Look(ref embeddingSnapshotPath, "embeddingSnapshotPath", string.Empty);
            Scribe_Values.Look(ref analysisReportPath, "analysisReportPath", string.Empty);
            Scribe_Values.Look(ref autoApplyMode, "autoApplyMode", FlywheelAutoApplyMode.Off);
            Scribe_Values.Look(ref autoApplyConfidenceThreshold, "autoApplyConfidenceThreshold", RimMindDefaults.AutoApplyConfidenceThreshold);
            Scribe_Values.Look(ref thinkCooldownTicks, "thinkCooldownTicks", RimMindDefaults.ThinkCooldownTicks);
            Scribe_Values.Look(ref agentTickInterval, "agentTickInterval", RimMindDefaults.AgentTickInterval);
            Scribe_Values.Look(ref maxToolCallDepth, "maxToolCallDepth", RimMindDefaults.DefaultMaxToolCallDepth);
            Scribe_Values.Look(ref requestExpireTicks, "requestExpireTicks", RimMindDefaults.RequestExpireTicks);
            Scribe_Values.Look(ref behaviorHistoryMax, "behaviorHistoryMax", RimMindDefaults.BehaviorHistoryMax);
            Scribe_Values.Look(ref queueProcessInterval, "queueProcessInterval", RimMindDefaults.QueueProcessInterval);
            Scribe_Values.Look(ref defaultModCooldownTicks, "defaultModCooldownTicks", RimMindDefaults.DefaultModCooldownTicks);
            Scribe_Values.Look(ref activityFrequencyScale, "activityFrequencyScale", RimMindDefaults.DefaultActivityFrequencyScale);
            Scribe_Values.Look(ref autoActivateColonistAgents, "autoActivateColonistAgents", false);
            Scribe_Values.Look(ref autonomyLevel, "autonomyLevel", AgentAutonomyLevel.Guided);
            Scribe_Values.Look(ref circuitBreakerFailureThreshold, "circuitBreakerFailureThreshold", RimMindDefaults.CircuitBreakerFailureThreshold);
            Scribe_Values.Look(ref circuitBreakerOpenDurationSec, "circuitBreakerOpenDurationSec", RimMindDefaults.CircuitBreakerOpenDurationSec);
            Validate();
        }

        public void Validate()
        {
            if (maxTokens < RimMindDefaults.MinTokens) maxTokens = RimMindDefaults.MinTokens;
            defaultTemperature = Math.Clamp(defaultTemperature, 0.0f, 2.0f);
            activityFrequencyScale = Math.Clamp(activityFrequencyScale, RimMindDefaults.MinActivityFrequencyScale, RimMindDefaults.MaxActivityFrequencyScale);
            if (maxConcurrentRequests < 1) maxConcurrentRequests = 1;
            if (requestTimeoutMs < RimMindDefaults.MinRequestTimeout) requestTimeoutMs = RimMindDefaults.MinRequestTimeout;
            if (thinkCooldownTicks < RimMindDefaults.MinQueueProcessInterval) thinkCooldownTicks = RimMindDefaults.MinQueueProcessInterval;
            if (agentTickInterval < 10) agentTickInterval = 10;
            if (maxToolCallDepth < 1) maxToolCallDepth = 1;
            if (contextDiffLifetimeTicks < RimMindDefaults.MinContextDiffLifetime) contextDiffLifetimeTicks = RimMindDefaults.MinContextDiffLifetime;
            if (circuitBreakerFailureThreshold < 1) circuitBreakerFailureThreshold = 1;
            if (circuitBreakerOpenDurationSec < 5) circuitBreakerOpenDurationSec = 5;
        }
    }
}
