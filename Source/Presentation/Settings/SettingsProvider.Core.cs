using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Enums;

namespace RimMind.Presentation.Settings
{
    internal sealed partial class SettingsProvider
    {
        // IQueueSettings
        public int QueueProcessInterval
        {
            get => _settings.queueProcessInterval;
            set => _settings.queueProcessInterval = value;
        }
        public int MaxConcurrentRequests
        {
            get => _settings.maxConcurrentRequests;
            set => _settings.maxConcurrentRequests = value;
        }
        public int RequestTimeoutMs
        {
            get => _settings.requestTimeoutMs;
            set => _settings.requestTimeoutMs = value;
        }
        public int MaxRetryCount
        {
            get => _settings.maxRetryCount;
            set => _settings.maxRetryCount = value;
        }
        public int RequestExpireTicks
        {
            get => _settings.requestExpireTicks;
            set => _settings.requestExpireTicks = value;
        }

        // IAgentAutonomySettings
        public AgentAutonomyLevel AutonomyLevel
        {
            get => _settings.autonomyLevel;
            set => _settings.autonomyLevel = value;
        }

        public bool ShouldApproveAction(RiskLevel risk)
        {
            return _settings.autonomyLevel switch
            {
                AgentAutonomyLevel.Manual => false,
                AgentAutonomyLevel.Guided => risk is RiskLevel.Low or RiskLevel.Medium,
                AgentAutonomyLevel.Autonomous => risk != RiskLevel.Critical,
                AgentAutonomyLevel.Full => true,
                _ => false
            };
        }

        // IAgentTickSettings
        public int AgentTickInterval => _settings.agentTickInterval;
        public int BehaviorHistoryMax
        {
            get => _settings.behaviorHistoryMax;
            set => _settings.behaviorHistoryMax = value;
        }
        public int ThinkCooldownTicks => _settings.thinkCooldownTicks;
        public int MaxToolCallDepth => _settings.maxToolCallDepth;
        public int DefaultModCooldownTicks
        {
            get => _settings.defaultModCooldownTicks;
            set => _settings.defaultModCooldownTicks = value;
        }

        // IAIModelSettings
        public int MaxTokens
        {
            get => _settings.maxTokens;
            set => _settings.maxTokens = value;
        }
        public float DefaultTemperature
        {
            get => _settings.defaultTemperature;
            set => _settings.defaultTemperature = value;
        }
        public bool ForceJsonMode
        {
            get => _settings.forceJsonMode;
            set => _settings.forceJsonMode = value;
        }
        public string ModelName
        {
            get => _settings.modelName ?? string.Empty;
            set => _settings.modelName = value;
        }

        // IApiCredentialSettings
        public string Provider
        {
            get => _settings.provider ?? string.Empty;
            set => _settings.provider = value;
        }
        public string ApiKey
        {
            get => _settings.apiKey ?? string.Empty;
            set => _settings.apiKey = value;
        }
        public string ApiEndpoint
        {
            get => _settings.apiEndpoint ?? string.Empty;
            set => _settings.apiEndpoint = value;
        }
        public string Player2RemoteUrl
        {
            get => _settings.player2RemoteUrl ?? string.Empty;
            set => _settings.player2RemoteUrl = value;
        }

        // IDebugSettings
        public bool DebugLogging
        {
            get => _settings.debugLogging;
            set => _settings.debugLogging = value;
        }

        // ICircuitBreakerSettings
        public int CircuitBreakerFailureThreshold => _settings.circuitBreakerFailureThreshold;
        public int CircuitBreakerOpenDurationSec => _settings.circuitBreakerOpenDurationSec;

        // IOverlaySettings
        public bool RequestOverlayEnabled
        {
            get => _settings.requestOverlayEnabled;
            set => _settings.requestOverlayEnabled = value;
        }
        public float RequestOverlayX
        {
            get => _settings.requestOverlayX;
            set => _settings.requestOverlayX = value;
        }
        public float RequestOverlayY
        {
            get => _settings.requestOverlayY;
            set => _settings.requestOverlayY = value;
        }
        public float RequestOverlayW
        {
            get => _settings.requestOverlayW;
            set => _settings.requestOverlayW = value;
        }
        public float RequestOverlayH
        {
            get => _settings.requestOverlayH;
            set => _settings.requestOverlayH = value;
        }
        public bool ShowAgentProgressFloat
        {
            get => _settings.showAgentProgressFloat;
            set => _settings.showAgentProgressFloat = value;
        }

        public void Persist() => RimMindCoreMod.Settings.Write();

        // IPromptSettings
        public string CustomPawnPrompt
        {
            get => _settings.customPawnPrompt ?? string.Empty;
            set => _settings.customPawnPrompt = value;
        }
        public string CustomMapPrompt
        {
            get => _settings.customMapPrompt ?? string.Empty;
            set => _settings.customMapPrompt = value;
        }

        // IFlywheelSettings
        public FlywheelAutoApplyMode AutoApplyMode
        {
            get => _settings.autoApplyMode;
            set => _settings.autoApplyMode = value;
        }
        public float AutoApplyConfidenceThreshold
        {
            get => _settings.autoApplyConfidenceThreshold;
            set => _settings.autoApplyConfidenceThreshold = value;
        }

        // IApiCredentialSettings
        public bool IsConfigured => _settings.IsConfigured();
        public bool IsOpenAIConfigured() => _settings.IsOpenAIConfigured();
    }
}
