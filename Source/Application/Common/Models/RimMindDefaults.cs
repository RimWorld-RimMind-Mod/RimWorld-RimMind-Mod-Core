namespace RimMind.Application.Common.Models
{
    /// <summary>
    /// Shared default values used across RimMind-Core.
    /// Centralizes magic numbers to ensure consistency and single source of truth.
    /// </summary>
    public static class RimMindDefaults
    {
        /// <summary>Default maximum tokens for AI requests.</summary>
        public const int MaxTokens = 800;

        /// <summary>Default request expiration time in game ticks.</summary>
        public const int RequestExpireTicks = 30000;

        /// <summary>Default token limit for test connection requests.</summary>
        public const int TestConnectionMaxTokens = 60;

        /// <summary>Default number of history records to retrieve.</summary>
        public const int DefaultHistoryLimit = 20;

        /// <summary>Default number of telemetry records to retrieve.</summary>
        public const int TelemetryRecordLimit = 100;

        /// <summary>Default string preview truncation length.</summary>
        public const int PreviewTruncateLength = 120;

        /// <summary>Default description truncation length.</summary>
        public const int DescriptionTruncateLength = 80;

        /// <summary>Default letter choice expiry in game ticks (≈60 seconds at 60 tps).</summary>
        public const int LetterChoiceExpireTicks = 3600;

        /// <summary>Default Agent tick interval in game ticks.</summary>
        public const int AgentTickInterval = 150;

        /// <summary>Default behavior history maximum entries.</summary>
        public const int BehaviorHistoryMax = 100;

        /// <summary>Default queue processing interval in game ticks.</summary>
        public const int QueueProcessInterval = 60;

        /// <summary>Default flywheel calibration interval in game ticks.</summary>
        public const int FlywheelCalibrateInterval = 10000;

        /// <summary>Default storage driver history query limit.</summary>
        public const int StorageHistoryLimit = 50;

        /// <summary>Default storage driver memory query limit.</summary>
        public const int StorageMemoryQueryLimit = 10;

        // Temperature defaults
        /// <summary>Default AI temperature for generation.</summary>
        public const float DefaultTemperature = 0.7f;

        /// <summary>Default confidence threshold for auto-apply decisions.</summary>
        public const float AutoApplyConfidenceThreshold = 0.8f;

        // Context budget defaults
        /// <summary>Default context budget ratio (fraction of total budget used for context).</summary>
        public const float DefaultContextBudget = 0.6f;

        /// <summary>Default total token budget for context orchestration.</summary>
        public const int DefaultTotalBudget = 4000;

        /// <summary>Default tokens reserved for output generation.</summary>
        public const int DefaultReserveForOutput = 800;

        /// <summary>Default character limit for brief context compression.</summary>
        public const int DefaultBriefLimit = 200;

        // Token estimation
        /// <summary>Token estimation multiplier for non-CJK characters.</summary>
        public const float TokenEstimateMultiplier = 4.0f;

        /// <summary>Token estimation divider for CJK characters.</summary>
        public const float TokenEstimateDivider = 1.5f;

        /// <summary>Token estimation minimum overhead ratio.</summary>
        public const float TokenEstimateMinRatio = 0.5f;

        // Perception thresholds
        /// <summary>Low importance threshold for perception entries.</summary>
        public const float PerceptionLowThreshold = 0.3f;

        /// <summary>Medium importance threshold for perception entries.</summary>
        public const float PerceptionMediumThreshold = 0.5f;

        /// <summary>High importance threshold for perception entries.</summary>
        public const float PerceptionHighThreshold = 0.7f;

        /// <summary>Critical importance threshold for perception entries.</summary>
        public const float PerceptionCriticalThreshold = 0.8f;

        // Context filter
        /// <summary>Minimum severity for hediffs to be included in context.</summary>
        public const float HediffSeverityFilter = 0.05f;

        // Budget weights
        /// <summary>Weight for context budget allocation (w2).</summary>
        public const float ContextBudgetWeight = 0.6f;

        /// <summary>Weight for reserve budget allocation (w1).</summary>
        public const float ReserveBudgetWeight = 0.4f;

        // Queue settings
        /// <summary>Game ticks per millisecond (for tick-to-ms conversion).</summary>
        public const int TicksPerMillisecond = 16;

        /// <summary>Number of failures before circuit breaker opens.</summary>
        public const int CircuitBreakerFailureThreshold = 5;

        /// <summary>Duration in seconds for circuit breaker to remain open.</summary>
        public const int CircuitBreakerOpenDurationSec = 60;

        /// <summary>Maximum number of context cache entries.</summary>
        public const int MaxCacheEntries = 100;

        // Player2 settings
        /// <summary>Default local port for Player2 app.</summary>
        public const int Player2LocalPort = 4315;

        /// <summary>Timeout in seconds for Player2 structured requests (local).</summary>
        public const int Player2StructuredTimeout = 300;

        /// <summary>Polling delay in milliseconds for Player2 structured requests.</summary>
        public const int Player2StructuredPollingDelay = 100;

        /// <summary>Timeout in seconds for Player2 balance queries.</summary>
        public const int Player2BalanceQueryTimeout = 10;

        /// <summary>Polling delay in milliseconds for Player2 balance queries.</summary>
        public const int Player2BalancePollingDelay = 100;

        /// <summary>Timeout in seconds for Player2 raw requests.</summary>
        public const int Player2RawRequestTimeout = 30;

        /// <summary>Polling delay in milliseconds for Player2 raw requests.</summary>
        public const int Player2RawRequestPollingDelay = 50;

        /// <summary>Health check interval in milliseconds for Player2.</summary>
        public const int Player2HealthCheckIntervalMs = 60000;

        /// <summary>Minimum interval in seconds between Player2 health checks.</summary>
        public const int Player2MinHealthCheckIntervalSec = 60;

        // UI settings
        /// <summary>Maximum number of history rounds displayed in UI.</summary>
        public const int MaxHistoryRounds = 20;

        // Request settings
        /// <summary>Default request timeout in milliseconds.</summary>
        public const int DefaultRequestTimeoutMs = 120000;

        // Agent
        /// <summary>Proactive agent mode tick interval in game ticks (≈2 game hours).</summary>
        public const int ProactiveTickInterval = 5000;

        /// <summary>Cooldown ticks between pawn think operations (≈1 game hour guardrail).</summary>
        public const int ThinkCooldownTicks = 2500;

        /// <summary>Timeout in game ticks before a think request is considered lost (≈30 seconds at 60 tps).</summary>
        public const int ThinkRequestTimeoutTicks = 1800;

        // History
        /// <summary>Maximum history entries per NPC.</summary>
        public const int MaxEntriesPerNpc = 200;

        // Debug
        /// <summary>Maximum entries in the AI debug log.</summary>
        public const int DebugMaxEntries = 200;

        // Queue
        /// <summary>Minimum queue processing interval in game ticks.</summary>
        public const int MinQueueProcessInterval = 60;

        /// <summary>Default mod cooldown in game ticks.</summary>
        public const int DefaultModCooldownTicks = 1500;

        // Context
        /// <summary>Minimum lifetime in ticks for context diff entries.</summary>
        public const int MinContextDiffLifetime = 600;

        /// <summary>Default lifetime in game ticks for context diff entries.</summary>
        public const int ContextDiffLifetimeTicks = 36000;

        // Goal progress
        /// <summary>Default delta for goal progress recording.</summary>
        public const float GoalProgressDelta = 0.1f;

        // Agent action
        /// <summary>Default cooldown in game ticks between pawn actions.</summary>
        public const int DefaultActionCooldown = 300;

        // History compression
        /// <summary>Threshold entry count for history compression.</summary>
        public const int HistoryCompressThreshold = 150;

        // Embed cache
        /// <summary>Maximum block entries in embed cache.</summary>
        public const int EmbedMaxBlockEntries = 200;

        /// <summary>Maximum entry items in embed cache.</summary>
        public const int EmbedMaxEntryItems = 500;

        /// <summary>Maximum embedding snapshot records per NPC.</summary>
        public const int EmbedMaxRecordsPerNpc = 500;

        /// <summary>Polling delay in milliseconds for health check requests.</summary>
        public const int HealthCheckPollingDelayMs = 100;

        /// <summary>Minimum allowed token count for AI requests.</summary>
        public const int MinTokens = 100;

        /// <summary>Minimum allowed request timeout in milliseconds.</summary>
        public const int MinRequestTimeout = 1000;

        // Telemetry
        /// <summary>Maximum telemetry records to retain.</summary>
        public const int TelemetryMaxRecords = 1000;

        // Perception buffer
        /// <summary>Maximum capacity of the perception buffer.</summary>
        public const int PerceptionBufferCapacity = 200;

        // Queue defaults
        /// <summary>Default maximum concurrent AI requests.</summary>
        public const int DefaultMaxConcurrentRequests = 4;

        /// <summary>Default queue request timeout in milliseconds.</summary>
        public const int QueueRequestTimeoutMs = 30000;

        /// <summary>Default maximum retry count for AI requests.</summary>
        public const int DefaultMaxRetryCount = 2;

        /// <summary>Default maximum tool call depth for AI requests.</summary>
        public const int DefaultMaxToolCallDepth = 3;

        // Activity & Frequency scaling defaults
        /// <summary>Default activity and event frequency scaling multiplier.</summary>
        public const float DefaultActivityFrequencyScale = 1.0f;
        public const float MinActivityFrequencyScale = 0.1f;
        public const float MaxActivityFrequencyScale = 3.5f;

        // Context environment
        /// <summary>Default environment scan radius in cells.</summary>
        public const int DefaultEnvironmentScanRadius = 5;

        /// <summary>Default maximum environment items to include.</summary>
        public const int DefaultEnvironmentMaxItems = 8;

        /// <summary>Default high threat threshold for wealth points.</summary>
        public const float DefaultThreatThresholdHigh = 200000f;

        /// <summary>Default medium threat threshold for wealth points.</summary>
        public const float DefaultThreatThresholdMedium = 100000f;

        /// <summary>Default low threat threshold for wealth points.</summary>
        public const float DefaultThreatThresholdLow = 50000f;

        /// <summary>Default minimum skill level for inclusion in context.</summary>
        public const int DefaultMinSkillLevel = 4;

        /// <summary>Default mood difference threshold for context updates.</summary>
        public const float DefaultMoodDiffThreshold = 5f;

        /// <summary>Default temperature difference threshold for context updates.</summary>
        public const float DefaultTemperatureDiffThreshold = 5f;

        /// <summary>
        /// Centralized middleware execution order constants.
        /// Lower values execute earlier in the pipeline.
        /// </summary>
        public static class MiddlewareOrder
        {
            public const int ShortCircuit = 10;
            public const int TraceContext = 20;
            public const int NpcEnrich = 30;
            public const int InputGuardrail = 35;
            public const int ContextBuild = 40;
            public const int ContextFeedback = 45;
            public const int RequestSanitize = 50;
            public const int Cache = 100;
            public const int Telemetry = 200;
            public const int CircuitBreaker = 300;
            public const int UnifiedRetry = 400;
            public const int ClientInvoke = 500;
            public const int ToolCallDispatch = 600;
            public const int Dispatch = 200;
            public const int OutputGuardrail = 650;
        }
    }
}
