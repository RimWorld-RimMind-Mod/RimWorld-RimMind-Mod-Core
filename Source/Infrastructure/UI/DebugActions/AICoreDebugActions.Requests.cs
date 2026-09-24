using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public static partial class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Test API Connection", actionType = DebugActionType.Action)]
        public static void TestConnection()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            if (!(runtimeScope.GetOptional<ISettingsProvider>()?.IsConfigured ?? false))
            {
                RimMindErrors.Warn("[RimMind-Core] API not configured. Set API Key in mod settings.");
                return;
            }

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("TestConnection")
                .WithModId("Debug")
                .WithMaxTokens(RimMindDefaults.TestConnectionMaxTokens)
                .WithTemperature(0f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            // Add test messages
            envelope.Messages.Add(new ChatMessage { Role = "system", Content = "You are a test assistant. Always reply in JSON format." });
            envelope.Messages.Add(new ChatMessage { Role = "user", Content = "Reply with: {\"status\":\"ok\",\"message\":\"RimMind works\"}" });

            RimMind.Presentation.Api.RimMindAPI.Send(envelope, result =>
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (result.IsOk)
                        Messages.Message("RimMind.Infrastructure.Debug.ConnectionSuccess".Translate(result.Value.Content ?? ""), MessageTypeDefOf.PositiveEvent, false);
                    else
                        Messages.Message("RimMind.Infrastructure.Debug.ConnectionFailed".Translate(result.Error.Message), MessageTypeDefOf.NegativeEvent, false);
                });
            });

            Messages.Message("RimMind.Infrastructure.Debug.RequestSent".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("RimMind", "Show Last Prompt", actionType = DebugActionType.Action)]
        public static void ShowLastPrompt()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var entries = runtimeScope.GetOptional<IAIRequestTraceLog>()?.Entries;
            if (entries == null || entries.Count == 0)
            {
                Log.Message("[RimMind-Core] No request trace records.");
                return;
            }
            var last = entries[entries.Count - 1];
            Log.Message($"[RimMind-Core] Last request trace ({last.Source}):\n" +
                        $"=== System Prompt ===\n{last.SystemPrompt}\n" +
                        $"=== User Prompt ===\n{last.UserPrompt}\n" +
                        $"=== Response ===\n{last.Response}\n" +
                        $"=== Error ===\n{last.Error ?? string.Empty}");
        }

        [DebugAction("RimMind", "Clear Debug Log", actionType = DebugActionType.Action)]
        public static void ClearLog()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            runtimeScope.GetOptional<IAIRequestTraceLog>()?.Clear();
            Log.Message("[RimMind-Core] Request trace log cleared.");
        }

        [DebugAction("RimMind", "Clear All Cooldowns", actionType = DebugActionType.Action)]
        public static void ClearCooldowns()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            runtimeScope.GetOptional<IRequestQueue>()?.ClearAllCooldowns();
            Log.Message("[RimMind-Core] All cooldowns cleared.");
        }

        [DebugAction("RimMind", "Show Queue State", actionType = DebugActionType.Action)]
        public static void ShowQueueState()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue == null)
            {
                RimMindErrors.Warn("[RimMind-Core] AIRequestQueue not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind-Core] === Queue State ===");
            sb.AppendLine($"  Paused: {queue.IsPaused}");
            sb.AppendLine($"  Active requests: {queue.ActiveRequestCount}");
            sb.AppendLine($"  Local model busy: {queue.IsLocalModelBusy}");

            var active = queue.GetActiveRequests();
            foreach (var t in active)
            {
                sb.AppendLine($"  [Active] {t.Envelope.RequestId} mod={t.Envelope.ModId} " +
                              $"priority={t.Envelope.Priority} state={t.State} attempt={t.AttemptCount}");
            }

            foreach (var kvp in queue.GetAllQueueDepths())
            {
                int cooldownLeft = queue.GetCooldownTicksLeft(kvp.Key);
                sb.AppendLine($"  [Queue] {kvp.Key}: depth={kvp.Value}, cooldown={cooldownLeft}t");
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Pause Queue", actionType = DebugActionType.Action)]
        public static void PauseQueue()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            runtimeScope.GetOptional<IRequestQueue>()?.PauseQueue();
            Log.Message("[RimMind-Core] Queue paused.");
        }

        [DebugAction("RimMind", "Resume Queue", actionType = DebugActionType.Action)]
        public static void ResumeQueue()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            runtimeScope.GetOptional<IRequestQueue>()?.ResumeQueue();
            Log.Message("[RimMind-Core] Queue resumed.");
        }

        [DebugAction("RimMind", "Show Settings Summary", actionType = DebugActionType.Action)]
        public static void ShowSettingsSummary()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var s = runtimeScope.GetOptional<ISettingsProvider>();
            if (s == null)
            {
                RimMindErrors.Warn("[RimMind-Core] Settings not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind-Core] === Settings Summary ===");
            sb.AppendLine($"  Provider: {s.Provider}");
            sb.AppendLine($"  Model: {s.ModelName}");
            sb.AppendLine($"  Endpoint: {s.ApiEndpoint}");
            sb.AppendLine($"  API Key: {(string.IsNullOrEmpty(s.ApiKey) ? "(empty)" : $"({s.ApiKey.Length} chars)")}");
            sb.AppendLine($"  ForceJsonMode: {s.ForceJsonMode}");
            sb.AppendLine($"  MaxTokens: {s.MaxTokens}");
            sb.AppendLine($"  DefaultTemperature: {s.DefaultTemperature}");
            sb.AppendLine($"  DebugLogging: {s.DebugLogging}");
            sb.AppendLine($"  MaxConcurrentRequests: {s.MaxConcurrentRequests}");
            sb.AppendLine($"  MaxRetryCount: {s.MaxRetryCount}");
            sb.AppendLine($"  RequestTimeoutMs: {s.RequestTimeoutMs}");
            sb.AppendLine($"  AutoApplyMode: (via Context)");
            sb.AppendLine($"  AutoApplyConfidenceThreshold: (via Context)");
            sb.AppendLine($"  RequestOverlayEnabled: (via UI)");
            sb.AppendLine($"  Player2RemoteUrl: {s.Player2RemoteUrl}");
            sb.AppendLine($"  TelemetryDataPath: (via Infrastructure)");
            sb.AppendLine($"  AnalysisReportPath: (via Infrastructure)");
            sb.AppendLine($"  IsConfigured: {s.IsConfigured}");

            Log.Message(sb.ToString());
        }
    }
}
