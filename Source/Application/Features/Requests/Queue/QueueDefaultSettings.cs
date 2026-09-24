using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Requests.Queue
{
    internal sealed class DefaultSettingsProvider : ISettingsProvider
    {
        private const int kCircuitBreakerFailureThreshold = RimMindDefaults.CircuitBreakerFailureThreshold;
        private const int kCircuitBreakerOpenDurationSec = RimMindDefaults.CircuitBreakerOpenDurationSec;
        private const int kMaxCacheEntries = RimMindDefaults.MaxCacheEntries;
        private const float kMoodDiffThreshold = RimMindDefaults.DefaultMoodDiffThreshold;
        private const float kTemperatureDiffThreshold = RimMindDefaults.DefaultTemperatureDiffThreshold;

        public int QueueProcessInterval { get => RimMindDefaults.QueueProcessInterval; set { } }
        public int MaxConcurrentRequests { get => RimMindDefaults.DefaultMaxConcurrentRequests; set { } }
        public int RequestTimeoutMs { get => RimMindDefaults.QueueRequestTimeoutMs; set { } }
        public int MaxRetryCount { get => RimMindDefaults.DefaultMaxRetryCount; set { } }
        public int RequestExpireTicks { get => RimMindDefaults.RequestExpireTicks; set { } }
        public int AgentTickInterval => RimMindDefaults.AgentTickInterval;
        public int BehaviorHistoryMax { get => RimMindDefaults.BehaviorHistoryMax; set { } }
        public int ThinkCooldownTicks => RimMindDefaults.ThinkCooldownTicks;
        public int MaxToolCallDepth => RimMindDefaults.DefaultMaxToolCallDepth;
        public int DefaultModCooldownTicks { get => RimMindDefaults.DefaultModCooldownTicks; set { } }
        public float ActivityFrequencyScale { get => RimMindDefaults.DefaultActivityFrequencyScale; set { } }
        public int MaxTokens { get => RimMindDefaults.MaxTokens; set { } }
        public float DefaultTemperature { get => RimMindDefaults.DefaultTemperature; set { } }
        public bool ForceJsonMode { get => true; set { } }
        public string ModelName { get => ""; set { } }
        public string Provider { get => AIProviderRegistry.GetDefaultProviderId(); set { } }
        public string ApiKey { get => ""; set { } }
        public string ApiEndpoint { get => ""; set { } }
        public string Player2RemoteUrl { get => ""; set { } }
        public bool DebugLogging { get => false; set { } }
        public int CircuitBreakerFailureThreshold => kCircuitBreakerFailureThreshold;
        public int CircuitBreakerOpenDurationSec => kCircuitBreakerOpenDurationSec;
        public int ContextCalibrateInterval { get => RimMindDefaults.FlywheelCalibrateInterval; set { } }
        public int ContextDiffLifetimeTicks { get => RimMindDefaults.ContextDiffLifetimeTicks; set { } }
        public bool IsConfigured => false;
        public IContextSettings Context => new DefaultContextSettings();
        public bool RequestOverlayEnabled { get => true; set { } }
        public float RequestOverlayX { get => 20f; set { } }
        public float RequestOverlayY { get => 20f; set { } }
        public float RequestOverlayW { get => 300f; set { } }
        public float RequestOverlayH { get => 200f; set { } }
        public bool ShowAgentProgressFloat { get => false; set { } }
        public bool RequestOverlayAutoHideWhenEmpty { get => true; set { } }
        public bool EnableFloatingMentalMonitor { get => false; set { } }
        public string CustomPawnPrompt { get => ""; set { } }
        public string CustomMapPrompt { get => ""; set { } }
        public FlywheelAutoApplyMode AutoApplyMode { get => FlywheelAutoApplyMode.Off; set { } }
        public float AutoApplyConfidenceThreshold { get => RimMindDefaults.AutoApplyConfidenceThreshold; set { } }
        public bool IsOpenAIConfigured() => false;
        public void Persist() { /* Null Object: defaults are immutable, nothing to persist */ }

        AgentAutonomyLevel IAgentAutonomySettings.AutonomyLevel { get => AgentAutonomyLevel.Autonomous; set { } }
        bool IAgentAutonomySettings.ShouldApproveAction(RiskLevel risk) => risk <= RiskLevel.Medium;

        bool IPawnIncludeSettings.IncludeRace { get => Context.IncludeRace; set => Context.IncludeRace = value; }
        bool IPawnIncludeSettings.IncludeAge { get => Context.IncludeAge; set => Context.IncludeAge = value; }
        bool IPawnIncludeSettings.IncludeGender { get => Context.IncludeGender; set => Context.IncludeGender = value; }
        bool IPawnIncludeSettings.IncludeBackstory { get => Context.IncludeBackstory; set => Context.IncludeBackstory = value; }
        bool IPawnIncludeSettings.IncludeIdeology { get => Context.IncludeIdeology; set => Context.IncludeIdeology = value; }
        bool IPawnIncludeSettings.IncludeTraits { get => Context.IncludeTraits; set => Context.IncludeTraits = value; }
        bool IPawnIncludeSettings.IncludeSkills { get => Context.IncludeSkills; set => Context.IncludeSkills = value; }
        int IPawnIncludeSettings.MinSkillLevel { get => Context.MinSkillLevel; set => Context.MinSkillLevel = value; }
        bool IPawnIncludeSettings.IncludeHealth { get => Context.IncludeHealth; set => Context.IncludeHealth = value; }
        bool IPawnIncludeSettings.IncludeCapacities { get => Context.IncludeCapacities; set => Context.IncludeCapacities = value; }
        bool IPawnIncludeSettings.IncludeMood { get => Context.IncludeMood; set => Context.IncludeMood = value; }
        bool IPawnIncludeSettings.IncludeMoodThoughts { get => Context.IncludeMoodThoughts; set => Context.IncludeMoodThoughts = value; }
        bool IPawnIncludeSettings.IncludeCurrentJob { get => Context.IncludeCurrentJob; set => Context.IncludeCurrentJob = value; }
        bool IPawnIncludeSettings.IncludeWorkPriorities { get => Context.IncludeWorkPriorities; set => Context.IncludeWorkPriorities = value; }
        bool IPawnIncludeSettings.IncludeEquipment { get => Context.IncludeEquipment; set => Context.IncludeEquipment = value; }
        bool IPawnIncludeSettings.IncludeInventory { get => Context.IncludeInventory; set => Context.IncludeInventory = value; }
        bool IPawnIncludeSettings.IncludeLocation { get => Context.IncludeLocation; set => Context.IncludeLocation = value; }
        bool IPawnIncludeSettings.IncludeRelations { get => Context.IncludeRelations; set => Context.IncludeRelations = value; }
        bool IPawnIncludeSettings.IncludeGenes { get => Context.IncludeGenes; set => Context.IncludeGenes = value; }
        bool IPawnIncludeSettings.IncludeSurroundings { get => Context.IncludeSurroundings; set => Context.IncludeSurroundings = value; }
        bool IPawnIncludeSettings.IncludeCombatStatus { get => Context.IncludeCombatStatus; set => Context.IncludeCombatStatus = value; }
        bool IMapIncludeSettings.IncludeGameTime { get => Context.IncludeGameTime; set => Context.IncludeGameTime = value; }
        bool IMapIncludeSettings.IncludeSeason { get => Context.IncludeSeason; set => Context.IncludeSeason = value; }
        bool IMapIncludeSettings.IncludeWeather { get => Context.IncludeWeather; set => Context.IncludeWeather = value; }
        bool IColonyIncludeSettings.IncludeColonistCount { get => Context.IncludeColonistCount; set => Context.IncludeColonistCount = value; }
        bool IColonyIncludeSettings.IncludeColonistNames { get => Context.IncludeColonistNames; set => Context.IncludeColonistNames = value; }
        bool IColonyIncludeSettings.IncludeWealth { get => Context.IncludeWealth; set => Context.IncludeWealth = value; }
        bool IColonyIncludeSettings.IncludeFood { get => Context.IncludeFood; set => Context.IncludeFood = value; }
        bool IColonyIncludeSettings.IncludeThreats { get => Context.IncludeThreats; set => Context.IncludeThreats = value; }

        float IContextBudgetSettings.ContextBudget { get => Context.ContextBudget; set => Context.ContextBudget = value; }
        int IContextBudgetSettings.ContextBriefLimit => Context.ContextBriefLimit;
        int IContextBudgetSettings.MaxCacheEntries => Context.MaxCacheEntries;

        int IContextEnvironmentSettings.EnvironmentScanRadius => Context.EnvironmentScanRadius;
        int IContextEnvironmentSettings.EnvironmentMaxItems => Context.EnvironmentMaxItems;
        float IContextEnvironmentSettings.ThreatThresholdHigh => Context.ThreatThresholdHigh;
        float IContextEnvironmentSettings.ThreatThresholdMedium => Context.ThreatThresholdMedium;
        float IContextEnvironmentSettings.ThreatThresholdLow => Context.ThreatThresholdLow;
        float IContextEnvironmentSettings.MoodDiffThreshold => Context.MoodDiffThreshold;
        float IContextEnvironmentSettings.TemperatureDiffThreshold => Context.TemperatureDiffThreshold;

        void IContextSettings.ApplyPreset(ContextPreset preset) => Context.ApplyPreset(preset);
        void IContextSettings.ResetToDefault() => Context.ResetToDefault();
    }

    internal sealed class DefaultContextSettings : IContextSettings
    {
        private const int kMaxCacheEntries = RimMindDefaults.MaxCacheEntries;
        private const float kMoodDiffThreshold = RimMindDefaults.DefaultMoodDiffThreshold;
        private const float kTemperatureDiffThreshold = RimMindDefaults.DefaultTemperatureDiffThreshold;

        public float ContextBudget { get => RimMindDefaults.DefaultContextBudget; set { } }
        public int ContextBriefLimit => RimMindDefaults.DefaultBriefLimit;
        public int EnvironmentScanRadius => RimMindDefaults.DefaultEnvironmentScanRadius;
        public int EnvironmentMaxItems => RimMindDefaults.DefaultEnvironmentMaxItems;
        public float ThreatThresholdHigh => RimMindDefaults.DefaultThreatThresholdHigh;
        public float ThreatThresholdMedium => RimMindDefaults.DefaultThreatThresholdMedium;
        public float ThreatThresholdLow => RimMindDefaults.DefaultThreatThresholdLow;
        public int MaxCacheEntries => kMaxCacheEntries;
        public float MoodDiffThreshold => kMoodDiffThreshold;
        public float TemperatureDiffThreshold => kTemperatureDiffThreshold;
        public bool IncludeRace { get => true; set { } }
        public bool IncludeAge { get => true; set { } }
        public bool IncludeGender { get => true; set { } }
        public bool IncludeBackstory { get => true; set { } }
        public bool IncludeIdeology { get => false; set { } }
        public bool IncludeTraits { get => true; set { } }
        public bool IncludeSkills { get => true; set { } }
        public int MinSkillLevel { get => RimMindDefaults.DefaultMinSkillLevel; set { } }
        public bool IncludeHealth { get => true; set { } }
        public bool IncludeCapacities { get => true; set { } }
        public bool IncludeMood { get => true; set { } }
        public bool IncludeMoodThoughts { get => false; set { } }
        public bool IncludeCurrentJob { get => true; set { } }
        public bool IncludeWorkPriorities { get => true; set { } }
        public bool IncludeEquipment { get => true; set { } }
        public bool IncludeInventory { get => false; set { } }
        public bool IncludeLocation { get => false; set { } }
        public bool IncludeRelations { get => true; set { } }
        public bool IncludeGenes { get => true; set { } }
        public bool IncludeSurroundings { get => false; set { } }
        public bool IncludeCombatStatus { get => true; set { } }
        public bool IncludeGameTime { get => true; set { } }
        public bool IncludeColonistCount { get => true; set { } }
        public bool IncludeColonistNames { get => true; set { } }
        public bool IncludeWealth { get => false; set { } }
        public bool IncludeFood { get => true; set { } }
        public bool IncludeSeason { get => true; set { } }
        public bool IncludeWeather { get => true; set { } }
        public bool IncludeThreats { get => true; set { } }
        public void ApplyPreset(ContextPreset preset) { /* Intentionally empty: Null Object pattern — defaults are immutable */ }
        public void ResetToDefault() { /* Intentionally empty: Null Object pattern — defaults are immutable */ }
    }
}
