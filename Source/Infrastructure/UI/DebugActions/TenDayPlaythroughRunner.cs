using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LudeonTK;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Debug;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugActions
{
    public sealed class PlaythroughColonistSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public float Mood { get; set; }
        public string CurrentJob { get; set; } = string.Empty;
        public string HealthSummary { get; set; } = string.Empty;
        public bool IsAgentActive { get; set; }
        public string AgentMode { get; set; } = string.Empty;
    }

    public sealed class PlaythroughAgentActionSummary
    {
        public string PawnName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ResultReason { get; set; } = string.Empty;
        public int Tick { get; set; }
    }

    public sealed class PlaythroughDayReport
    {
        public int DayNumber { get; set; }
        public int Day => DayNumber;
        public int StartTick { get; set; }
        public int EndTick { get; set; }
        public string DateString { get; set; } = string.Empty;
        public List<PlaythroughColonistSnapshot> Colonists { get; set; } = new();
        public string MorningPawnName { get; set; } = string.Empty;
        public string MorningThought { get; set; } = string.Empty;
        public string ColonyOperationName { get; set; } = string.Empty;
        public string ColonyOperationDetail { get; set; } = string.Empty;
        public bool ColonyOperationSuccess { get; set; }
        public string DialogueSpeaker { get; set; } = string.Empty;
        public string DialogueListener { get; set; } = string.Empty;
        public string DialogueSpeech { get; set; } = string.Empty;
        public string DialogueThoughtTag { get; set; } = string.Empty;
        public int DialogueRelationDelta { get; set; }
        public int DialogueCount { get; set; }
        public string AgentActionPawn { get; set; } = string.Empty;
        public string AgentActionTool { get; set; } = string.Empty;
        public string AgentActionDetail { get; set; } = string.Empty;
        public string AgentActionReason { get; set; } = string.Empty;
        public int AgentActionsExecutedCount { get; set; }
        public List<PlaythroughAgentActionSummary> AgentActions { get; set; } = new();
        public string AdvisorProposal { get; set; } = string.Empty;
        public bool AdvisorApproved { get; set; }
        public int WorkingMemoryCount { get; set; }
        public int EpisodicMemoryCount { get; set; }
        public int ColonyFactCount { get; set; }
        public int ActiveMemories
        {
            get => WorkingMemoryCount;
            set => WorkingMemoryCount = value;
        }
        public int ArchiveMemories
        {
            get => EpisodicMemoryCount;
            set => EpisodicMemoryCount = value;
        }
        public int DarkMemories
        {
            get => ColonyFactCount;
            set => ColonyFactCount = value;
        }
        public int TotalTokensUsed { get; set; }
        public int TokensUsed
        {
            get => TotalTokensUsed;
            set => TotalTokensUsed = value;
        }
        public int LlmCallsCount { get; set; }
        public int SuccessfulLlmCallsCount { get; set; }
        public Dictionary<string, int> LlmCallsBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int PrefixTokens { get; set; }
        public double CacheHitRatio { get; set; }
        public string ScreenshotPath { get; set; } = string.Empty;
        public long DayComputeDurationMs { get; set; }
    }

    public sealed class TenDayPlaythroughReport
    {
        public string RunId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int TotalDaysCompleted { get; set; }
        public int TotalTicksElapsed { get; set; }
        public long TotalDurationMs { get; set; }
        public int TotalLiveLlmRequests { get; set; }
        public int SuccessfulLiveLlmRequests { get; set; }
        public int TotalTokensConsumed { get; set; }
        public int TotalPrefixTokens { get; set; }
        public double CacheHitRatio { get; set; }
        public double AverageCacheHitRatio { get; set; }
        public Dictionary<string, int> LlmCallsBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PlaythroughDayReport> Days { get; set; } = new();
        public string OverallStatus { get; set; } = "RUNNING";
        public string SummaryNotes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Genuine in-game runtime test runner for RimMind.
    /// Drives native RimWorld simulation ticks and observes real Mod subsystem outputs
    /// (PawnAgent tool execution, RimMind-Dialogue, RimMind-Personality, RimMind-Advisor, RimMind-Memory)
    /// without injecting synthetic LLM prompts, scripted base-building, or pawn stat cheating.
    /// </summary>
    public static class TenDayPlaythroughRunner
    {
        private static PlaythroughCoroutineHost? _activeHost;

        [DebugAction("RimMind", "Run 10-Day AI Playthrough", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StartPlaythrough()
        {
            int defaultDays = ResolveConfiguredDays(10);
            StartPlaythrough(null, defaultDays);
        }

        public static void StartPlaythrough(int totalDays)
        {
            StartPlaythrough(null, totalDays);
        }

        public static void StartPlaythrough(string? runId, int totalDays = 10)
        {
            if (_activeHost != null)
            {
                Log.Warning("[RimMind-Playthrough] Playthrough runner is already running.");
                return;
            }

            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                Log.Warning("[RimMind-Playthrough] Playthrough requires a loaded map.");
                return;
            }

            int clampedDays = Math.Clamp(totalDays > 0 ? totalDays : ResolveConfiguredDays(10), 1, 100);
            string resolvedRunId = !string.IsNullOrWhiteSpace(runId)
                ? runId!.Trim()
                : $"pt{clampedDays}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            PlaythroughCoroutineHost? host = null;
            try
            {
                host = Current.Root.gameObject.AddComponent<PlaythroughCoroutineHost>();
                _activeHost = host;
                host.Begin(resolvedRunId, clampedDays);
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Playthrough] Failed to initialize playthrough runner: " + ex);
                _activeHost = null;
                if (host != null)
                {
                    host.RestorePlayerSettings();
                    UnityEngine.Object.Destroy(host);
                }
            }
        }

        private static int ResolveConfiguredDays(int fallbackDays)
        {
            string? envDays = Environment.GetEnvironmentVariable("RIMMIND_PLAYTHROUGH_DAYS");
            if (!string.IsNullOrWhiteSpace(envDays) && int.TryParse(envDays, out int parsed) && parsed > 0)
            {
                return Math.Clamp(parsed, 1, 100);
            }
            return Math.Clamp(fallbackDays, 1, 100);
        }

        private sealed class ApprovedOverlayItem
        {
            public string Source { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Choice { get; set; } = string.Empty;
            public int Tick { get; set; }
        }

        private sealed class PlaythroughCoroutineHost : MonoBehaviour
        {
            private string _runId = string.Empty;
            private int _totalDays = 10;
            private string _outputDir = string.Empty;
            private string _legacyRunOutputDir = string.Empty;
            private string _bridgeDir = string.Empty;
            private bool _interactiveBridgeEnabled;
            private TenDayPlaythroughReport _report = new();
            private readonly Stopwatch _clock = new();
            private SettingsOverrideScope? _settingsScope;

            private readonly HashSet<string> _processedTraceRequestIds = new(StringComparer.Ordinal);
            private readonly List<ApprovedOverlayItem> _approvedOverlayItems = new();
            private int _totalLlmCalls;
            private int _successfulLlmCalls;
            private int _totalTokens;
            private int _totalPromptTokens;
            private int _totalPrefixTokens;

            public void Begin(string runId, int totalDays)
            {
                _runId = runId;
                _totalDays = Math.Clamp(totalDays, 1, 100);
                _clock.Start();

                _settingsScope = CoreSettingsOverrideScope.Begin("Playthrough");
                _settingsScope.TrackExternal(() => Prefs.DevMode, v => Prefs.DevMode = v);
                _settingsScope.TrackExternal(() => Prefs.AdaptiveTrainingEnabled, v => Prefs.AdaptiveTrainingEnabled = v);

                ApplyEnvironmentCredentialsIfProvided();
                ConfigureOutputDirectories();
                InitializeGenuineModSettings();

                _report = new TenDayPlaythroughReport
                {
                    RunId = _runId,
                    Provider = RimMindCoreMod.Settings.provider,
                    Endpoint = RimMindCoreMod.Settings.apiEndpoint,
                    Model = RimMindCoreMod.Settings.modelName
                };

                Log.Message("[RimMind-Playthrough] ========================================================");
                Log.Message($"[RimMind-Playthrough] Starting {_totalDays}-Day Genuine In-Game AI Playthrough");
                Log.Message($"[RimMind-Playthrough] RunId: {_runId}");
                Log.Message($"[RimMind-Playthrough] Endpoint: {RimMindCoreMod.Settings.apiEndpoint}, Model: {RimMindCoreMod.Settings.modelName}");
                Log.Message($"[RimMind-Playthrough] OutputDir: {_outputDir}");
                Log.Message("[RimMind-Playthrough] ========================================================");

                StartCoroutine(RunPlaythroughThenRestore());
            }

            public void RestorePlayerSettings() => CoreSettingsOverrideScope.End(ref _settingsScope);

            private void ApplyEnvironmentCredentialsIfProvided()
            {
                string? envKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
                string? envEndpoint = Environment.GetEnvironmentVariable("RIMMIND_TEST_ENDPOINT");
                string? envModel = Environment.GetEnvironmentVariable("RIMMIND_TEST_MODEL");

                if (!string.IsNullOrWhiteSpace(envKey))
                    RimMindCoreMod.Settings.apiKey = envKey!.Trim();
                if (!string.IsNullOrWhiteSpace(envEndpoint))
                    RimMindCoreMod.Settings.apiEndpoint = envEndpoint!.Trim();
                if (!string.IsNullOrWhiteSpace(envModel))
                    RimMindCoreMod.Settings.modelName = envModel!.Trim();

                string? backupKey = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_API_KEY");
                string? backupEndpoint = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_ENDPOINT");
                string? backupModel = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_MODEL");
                string? backupProvider = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_PROVIDER");

                if (!string.IsNullOrWhiteSpace(backupKey))
                {
                    string resolvedEndpoint = !string.IsNullOrWhiteSpace(backupEndpoint)
                        ? backupEndpoint!.Trim()
                        : "https://opencode.ai/zen/go/v1";
                    string resolvedModel = !string.IsNullOrWhiteSpace(backupModel)
                        ? backupModel!.Trim()
                        : "beam-free";
                    string resolvedProvider = !string.IsNullOrWhiteSpace(backupProvider)
                        ? backupProvider!.Trim()
                        : "opencode";

                    RimMindCoreMod.Settings.backupEndpoints ??= new List<BackupEndpointEntry>();
                    var existing = RimMindCoreMod.Settings.backupEndpoints.FirstOrDefault(b =>
                        b != null &&
                        string.Equals(b.apiEndpoint?.Trim().TrimEnd('/'), resolvedEndpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(b.modelName?.Trim(), resolvedModel, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.apiKey = backupKey!.Trim();
                        existing.provider = resolvedProvider;
                        existing.enabled = true;
                    }
                    else
                    {
                        RimMindCoreMod.Settings.backupEndpoints.Insert(0, new BackupEndpointEntry
                        {
                            name = "OpenCode Go (Backup)",
                            provider = resolvedProvider,
                            apiEndpoint = resolvedEndpoint,
                            modelName = resolvedModel,
                            apiKey = backupKey!.Trim(),
                            enabled = true
                        });
                    }
                }

                FilterIncompatibleBackupNodes(RimMindCoreMod.Settings);
            }

            private static void FilterIncompatibleBackupNodes(RimMindCoreSettings? settings)
            {
                if (settings?.backupEndpoints == null || settings.backupEndpoints.Count == 0)
                    return;

                bool hasHealthyPrimary =
                    !string.IsNullOrWhiteSpace(settings.apiEndpoint) &&
                    !string.IsNullOrWhiteSpace(settings.apiKey) &&
                    settings.apiEndpoint.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) < 0 &&
                    settings.apiEndpoint.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) < 0;

                bool hasHealthyBackup = settings.backupEndpoints.Any(n =>
                    n != null &&
                    n.enabled &&
                    !string.IsNullOrWhiteSpace(n.apiEndpoint) &&
                    !string.IsNullOrWhiteSpace(n.apiKey) &&
                    n.apiEndpoint.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.apiEndpoint.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                    (string.IsNullOrWhiteSpace(n.modelName) || n.modelName.IndexOf("grok-", StringComparison.OrdinalIgnoreCase) < 0));

                if (!hasHealthyPrimary && !hasHealthyBackup)
                    return;

                int disabledCount = 0;
                for (int i = 0; i < settings.backupEndpoints.Count; i++)
                {
                    var node = settings.backupEndpoints[i];
                    if (node == null || !node.enabled || string.IsNullOrWhiteSpace(node.apiEndpoint))
                        continue;

                    bool isDeadProvider =
                        node.apiEndpoint.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        node.apiEndpoint.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool isUnstableGrokBackup =
                        hasHealthyPrimary &&
                        !string.IsNullOrWhiteSpace(node.modelName) &&
                        node.modelName.IndexOf("grok-", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isDeadProvider || isUnstableGrokBackup)
                    {
                        node.enabled = false;
                        disabledCount++;
                    }
                }

                if (disabledCount > 0)
                {
                    Log.Message($"[RimMind-Playthrough] Disabled {disabledCount} quota-exhausted/incompatible backup node(s) (opencode.ai / deepseek.com / grok-*).");
                }
            }

            private void ConfigureOutputDirectories()
            {
                _legacyRunOutputDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "PlaythroughTests", _runId);

                string? customOut = Environment.GetEnvironmentVariable("RIMMIND_PLAYTHROUGH_OUTPUT_DIR");
                _outputDir = !string.IsNullOrWhiteSpace(customOut)
                    ? customOut!.Trim()
                    : _legacyRunOutputDir;

                string? customBridge = Environment.GetEnvironmentVariable("RIMMIND_BRIDGE_DIR");
                _bridgeDir = !string.IsNullOrWhiteSpace(customBridge)
                    ? customBridge!.Trim()
                    : Path.Combine(_outputDir, "bridge");

                EnsureDirectoryExists(_outputDir);
                EnsureDirectoryExists(_legacyRunOutputDir);
                EnsureDirectoryExists(_bridgeDir);

                string? bridgeEnv = Environment.GetEnvironmentVariable("RIMMIND_INTERACTIVE_BRIDGE");
                _interactiveBridgeEnabled = GenCommandLine.CommandLineArgPassed("rimmind-interactive-bridge") ||
                                            string.Equals(bridgeEnv, "1", StringComparison.Ordinal) ||
                                            string.Equals(bridgeEnv, "true", StringComparison.OrdinalIgnoreCase);
                Log.Message($"[RimMind-Playthrough] Interactive Sub Blind Agent bridge: {(_interactiveBridgeEnabled ? "ENABLED" : "disabled")} ({_bridgeDir})");
            }

            private static void EnsureDirectoryExists(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough] Failed to create directory '{path}': {ex.Message}");
                }
            }

            private static void InitializeGenuineModSettings()
            {
                try
                {
                    Prefs.AdaptiveTrainingEnabled = false;
                    UnityEngine.Application.runInBackground = true;
                    UnityEngine.QualitySettings.vSyncCount = 0;
                    UnityEngine.Application.targetFrameRate = 120;
                }
                catch { }

                Prefs.DevMode = true;

                var settings = RimMindCoreMod.Settings;
                settings.requestOverlayEnabled = true;
                settings.requestOverlayX = 155f;
                settings.requestOverlayY = 100f;
                settings.showAgentProgressFloat = true;
                settings.enableFloatingMentalMonitor = true;
                settings.autonomyLevel = AgentAutonomyLevel.Autonomous;
                settings.thinkCooldownTicks = RimMind.Application.Common.Models.RimMindDefaults.ThinkCooldownTicks;
                settings.agentTickInterval = RimMind.Application.Common.Models.RimMindDefaults.AgentTickInterval;
                settings.defaultModCooldownTicks = RimMind.Application.Common.Models.RimMindDefaults.DefaultModCooldownTicks;
                settings.activityFrequencyScale = RimMind.Application.Common.Models.RimMindDefaults.DefaultActivityFrequencyScale;

                RequestOverlay.SetWindowRectForTest(new Rect(
                    settings.requestOverlayX,
                    settings.requestOverlayY,
                    settings.requestOverlayW,
                    settings.requestOverlayH));
            }

            private IEnumerator RunPlaythroughThenRestore()
            {
                yield return SafeRunSubRoutine(PlaythroughRoutine(_totalDays), "PlaythroughMain", 0);
                RestorePlayerSettings();
            }

            private static IEnumerator SafeRunSubRoutine(IEnumerator? routine, string stepName, int day)
            {
                if (routine == null) yield break;
                var stack = new Stack<IEnumerator>();
                stack.Push(routine);
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    bool moved = false;
                    object? current = null;
                    try
                    {
                        moved = top.MoveNext();
                        if (moved) current = top.Current;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimMind-Playthrough][Day {day}] {stepName} handled exception ({ex.GetType().Name}): {ex.Message}");
                        stack.Pop();
                        continue;
                    }

                    if (!moved)
                    {
                        stack.Pop();
                        continue;
                    }

                    if (current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }

                    yield return current;
                }
            }

            private IEnumerator PlaythroughRoutine(int totalDays)
            {
                InitializeDayOneMapAndColonists();

                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }

                SeedExistingTraceEntries();

                int startTick = Find.TickManager?.TicksGame ?? 0;
                Log.Message($"[RimMind-Playthrough] Simulation start at Tick {startTick}");

                InitializeDayOneMapAndColonists();

                // Simulated player's first click: unforbid the crashlanded cargo.
                PlaythroughPlayerSimulation.UnforbidStartingItems(Find.CurrentMap);

                for (int day = 1; day <= totalDays; day++)
                {
                    var daySw = Stopwatch.StartNew();
                    int dayStartTick = Find.TickManager?.TicksGame ?? 0;
                    int dayEndTick = dayStartTick + GenDate.TicksPerDay;
                    string dateStr = Find.CurrentMap != null && Find.WorldGrid != null
                        ? GenDate.DateFullStringAt(GenTicks.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile))
                        : $"Day {day}";

                    Log.Message($"[RimMind-Playthrough] >>> Starting Day {day}/{totalDays} (GameTick: {dayStartTick}, Date: {dateStr}) <<<");

                    EnsureColonistAgentSplitAndWorkSettings();

                    yield return SafeRunSubRoutine(AdvanceDayTicksRoutine(day, dayStartTick, dayEndTick), "AdvanceDayTicks", day);

                    int actualEndTick = Find.TickManager?.TicksGame ?? dayEndTick;
                    var dayReport = CollectDayReport(day, dayStartTick, actualEndTick, dateStr);

                    yield return SafeRunSubRoutine(CaptureDayScreenshotRoutine(day, dayReport), "CaptureDayScreenshot", day);

                    daySw.Stop();
                    dayReport.DayComputeDurationMs = daySw.ElapsedMilliseconds;

                    ExportDailyStateJson(day, dayReport, Find.CurrentMap);

                    if (_interactiveBridgeEnabled)
                    {
                        yield return SafeRunSubRoutine(WaitForPlayerCommandRoutine(day, dayReport), "PlayerCommandBridge", day);
                    }

                    _report.Days.Add(dayReport);
                    _report.TotalDaysCompleted = day;
                    _report.TotalLiveLlmRequests = _totalLlmCalls;
                    _report.SuccessfulLiveLlmRequests = _successfulLlmCalls;
                    _report.TotalTokensConsumed = _totalTokens;
                    _report.TotalPrefixTokens = _totalPrefixTokens;
                    _report.CacheHitRatio = _totalPromptTokens > 0
                        ? Math.Round((double)_totalPrefixTokens * 100.0 / _totalPromptTokens, 1)
                        : 0.0;
                    _report.AverageCacheHitRatio = _report.CacheHitRatio;
                    _report.TotalTicksElapsed = actualEndTick - startTick;
                    _report.TotalDurationMs = _clock.ElapsedMilliseconds;

                    SaveReportCheckpoint();

                    Log.Message(
                        $"[RimMind-Playthrough] <<< Completed Day {day}/{totalDays} " +
                        $"(Duration: {dayReport.DayComputeDurationMs}ms, LLMCalls: {dayReport.LlmCallsCount}, " +
                        $"AgentActions: {dayReport.AgentActionsExecutedCount}, Tokens: {dayReport.TotalTokensUsed}, " +
                        $"PrefixTokens: {dayReport.PrefixTokens}, CacheHitRatio: {dayReport.CacheHitRatio:F1}%) >>>");

                    yield return null;
                }

                _report.TotalTicksElapsed = (Find.TickManager?.TicksGame ?? startTick) - startTick;
                _report.TotalDurationMs = _clock.ElapsedMilliseconds;
                _report.TotalLiveLlmRequests = _totalLlmCalls;
                _report.SuccessfulLiveLlmRequests = _successfulLlmCalls;
                _report.TotalTokensConsumed = _totalTokens;
                _report.TotalPrefixTokens = _totalPrefixTokens;
                _report.CacheHitRatio = _totalPromptTokens > 0
                    ? Math.Round((double)_totalPrefixTokens * 100.0 / _totalPromptTokens, 1)
                    : 0.0;
                _report.AverageCacheHitRatio = _report.CacheHitRatio;
                _report.OverallStatus = "COMPLETED";
                _report.SummaryNotes =
                    $"{totalDays}-day genuine playthrough completed across {_report.TotalTicksElapsed} ticks with " +
                    $"{_report.TotalLiveLlmRequests} real Mod LLM requests ({_report.SuccessfulLiveLlmRequests} succeeded), " +
                    $"{_report.TotalTokensConsumed} tokens, and KV-cache hit ratio {_report.CacheHitRatio:F1}%.";

                SaveReportCheckpoint();
                GeneratePlaythroughChronicle();

                Log.Message($"[RimMind-Playthrough] === {totalDays}-DAY AI PLAYTHROUGH COMPLETE ===");
                Log.Message($"[RimMind-Playthrough] Status: {_report.OverallStatus}, Duration: {_report.TotalDurationMs}ms, Total LLM Calls: {_report.TotalLiveLlmRequests}, Tokens: {_report.TotalTokensConsumed}");

                bool autoExit = Environment.GetEnvironmentVariable("RIMMIND_PLAYTHROUGH_AUTO_EXIT") == "1" ||
                                GenCommandLine.CommandLineArgPassed("rimmind-autotest") ||
                                GenCommandLine.CommandLineArgPassed("rimmind-playthrough") ||
                                GenCommandLine.CommandLineArgPassed("rimmind-10day-playthrough");

                if (autoExit)
                {
                    yield return new WaitForSecondsRealtime(2f);
                    RestorePlayerSettings();
                    Root.Shutdown();
                }
            }

            /// <summary>
            /// Pauses the game after the day-N snapshot and waits for the external Sub Blind Agent
            /// (simulated player) to write <c>cmd_day_NN.json</c>. On timeout nothing is executed:
            /// there is no scripted fallback, the colony simply continues under Mod/vanilla control.
            /// </summary>
            private IEnumerator WaitForPlayerCommandRoutine(int day, PlaythroughDayReport report)
            {
                float timeoutLimit = 90f;
                string? timeoutEnv = Environment.GetEnvironmentVariable("RIMMIND_BRIDGE_TIMEOUT");
                if (!string.IsNullOrEmpty(timeoutEnv) && float.TryParse(timeoutEnv, out float customTimeout) && customTimeout > 10f)
                {
                    timeoutLimit = customTimeout;
                }

                string cmdPath = Path.Combine(_bridgeDir, $"cmd_day_{day:D2}.json");
                string donePath = Path.Combine(_bridgeDir, $"cmd_day_{day:D2}.done");
                string ackPath = Path.Combine(_bridgeDir, $"ack_day_{day:D2}.json");

                var previousSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Normal;
                Log.Message($"[RimMind-Playthrough][Day {day}] Waiting up to {timeoutLimit}s for simulated-player command {Path.GetFileName(cmdPath)}...");

                float waitStart = Time.realtimeSinceStartup;
                ColonySubagentCommand? cmd = null;
                while (Time.realtimeSinceStartup - waitStart < timeoutLimit)
                {
                    if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

                    if (File.Exists(cmdPath))
                    {
                        try
                        {
                            cmd = JsonConvert.DeserializeObject<ColonySubagentCommand>(File.ReadAllText(cmdPath, Encoding.UTF8));
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to parse player command: {ex.Message}");
                        }

                        if (cmd != null)
                        {
                            try
                            {
                                if (File.Exists(donePath)) File.Delete(donePath);
                                File.Move(cmdPath, donePath);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to mark command done: {ex.Message}");
                            }
                            break;
                        }
                    }

                    yield return new WaitForSecondsRealtime(0.25f);
                }

                bool success = false;
                string detail;
                if (cmd == null)
                {
                    detail = $"no player command within {timeoutLimit}s (no fallback executed)";
                }
                else if (Find.CurrentMap == null)
                {
                    detail = "no current map";
                }
                else
                {
                    (success, detail) = PlaythroughPlayerSimulation.ExecuteCommand(cmd, Find.CurrentMap);
                }

                report.ColonyOperationName = cmd?.Action ?? "none";
                report.ColonyOperationDetail = cmd == null
                    ? detail
                    : $"{cmd.Reason} => {detail}" + (string.IsNullOrWhiteSpace(cmd.UxFinding) ? string.Empty : $" | UX: {cmd.UxFinding}");
                report.ColonyOperationSuccess = success;

                Log.Message($"{PlaythroughPlayerSimulation.LogTag}[Day {day}] action={report.ColonyOperationName} target={cmd?.Target} success={success} detail={detail}");

                if (cmd != null)
                {
                    try
                    {
                        var ack = new
                        {
                            status = success ? "OK" : "FAILED",
                            day,
                            action = cmd.Action,
                            target = cmd.Target,
                            detail,
                            timestamp = DateTime.UtcNow.ToString("o")
                        };
                        File.WriteAllText(ackPath, JsonConvert.SerializeObject(ack, Formatting.Indented), new UTF8Encoding(false));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to write ack: {ex.Message}");
                    }
                }

                if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = previousSpeed == TimeSpeed.Paused ? TimeSpeed.Ultrafast : previousSpeed;
            }

            private void SeedExistingTraceEntries()
            {
                var scope = RuntimeServiceHub.Shared.Capture();
                var traceLog = scope.GetOptional<IAIRequestTraceLog>();
                if (traceLog?.Entries == null) return;

                foreach (var entry in traceLog.Entries)
                {
                    if (entry != null &&
                        !string.IsNullOrEmpty(entry.RequestId) &&
                        (entry.State == AIRequestTraceState.Completed || entry.State == AIRequestTraceState.Failed))
                    {
                        _processedTraceRequestIds.Add(entry.RequestId);
                    }
                }
            }

            private static void InitializeDayOneMapAndColonists()
            {
                EnsureColonistAgentSplitAndWorkSettings();
            }

            private static List<Pawn> GetActiveSpawnedColonists()
            {
                var map = Find.CurrentMap;
                if (map == null) return new List<Pawn>();
                return RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                    .Where(p => p != null && RimMindPawnLookup.IsActiveSpawnedColonist(p))
                    // Capable pawns first so the PawnAgent split never lands on a downed colonist.
                    .OrderBy(p => p.Downed ? 1 : 0)
                    .ThenBy(p => p.thingIDNumber)
                    .ToList();
            }

            private static List<Pawn> EnsureColonistAgentSplitAndWorkSettings()
            {
                var colonists = GetActiveSpawnedColonists();
                if (colonists.Count == 0) return colonists;

                try
                {
                    if (Current.Game?.playSettings != null)
                    {
                        Current.Game.playSettings.useWorkPriorities = true;
                    }

                    int activeAgentTargetCount = colonists.Count >= 2
                        ? Math.Max(1, colonists.Count - 1)
                        : 1;

                    var modeColonist = new AgentModeId("colonist");
                    bool hasColonistMode = RimMindAPI.Modes.FindById(modeColonist.Value) != null;
                    bool hasProactiveMode = RimMindAPI.Modes.FindById(AgentModeId.Proactive.Value) != null;

                    for (int i = 0; i < colonists.Count; i++)
                    {
                        var pawn = colonists[i];
                        if (pawn == null || pawn.Dead || !pawn.Spawned) continue;

                        RimMindPawnLookup.CachePawn(pawn);
                        if (pawn.workSettings != null && pawn.workSettings.EverWork)
                        {
                            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                        }

                        var comp = CompPawnAgent.GetComp(pawn);
                        if (comp == null) continue;

                        bool shouldBeActiveAgent = i < activeAgentTargetCount;
                        if (shouldBeActiveAgent)
                        {
                            comp.EnsureAgentCreated();
                            foreach (var domain in (AutonomyDomain[])Enum.GetValues(typeof(AutonomyDomain)))
                            {
                                comp.AutonomyPolicy.SetLevel(domain, AutonomyLevel.FullAutonomous);
                            }

                            if (comp.Agent != null)
                            {
                                if (comp.Agent is RimMind.Presentation.Agent.PawnAgent concreteAgent)
                                {
                                    concreteAgent.AutonomyLevel = AgentAutonomyLevel.Autonomous;
                                }

                                if (comp.Agent.State != AgentState.Active)
                                {
                                    comp.Agent.TransitionTo(AgentState.Active);
                                    Log.Message($"[RimMind-Playthrough] Activated autonomous PawnAgent on {pawn.Name?.ToStringShort} (NPC-{pawn.thingIDNumber}).");
                                }

                                if (hasColonistMode && comp.Agent.CurrentModeId != modeColonist)
                                {
                                    comp.Agent.SwitchMode(modeColonist);
                                }
                                else if (hasProactiveMode && comp.Agent.CurrentModeId != AgentModeId.Proactive)
                                {
                                    comp.Agent.SwitchMode(AgentModeId.Proactive);
                                }
                            }
                        }
                        else
                        {
                            if (comp.Agent != null && comp.Agent.State == AgentState.Active)
                            {
                                comp.Agent.TransitionTo(AgentState.Paused);
                                Log.Message($"[RimMind-Playthrough] Left {pawn.Name?.ToStringShort} (NPC-{pawn.thingIDNumber}) non-active for Personality/Advisor/Dialogue coverage.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough] EnsureColonistAgentSplitAndWorkSettings notice: {ex.Message}");
                }

                return colonists;
            }

            private IEnumerator AdvanceDayTicksRoutine(int day, int dayStartTick, int dayEndTick)
            {
                if (Find.TickManager == null) yield break;

                // Native RimWorld time flow only (TimeSpeed.Ultrafast, as a player pressing "4" in dev mode).
                // No manual DoSingleTick stacking: the game must not outrun what a real session allows,
                // otherwise PawnAgent LLM decisions arrive days late and the test stops reflecting play.
                UnityEngine.Application.runInBackground = true;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                int lastMaintenanceTick = dayStartTick;

                while (Find.TickManager.TicksGame < dayEndTick)
                {
                    int nowTick = Find.TickManager.TicksGame;
                    if (nowTick - lastMaintenanceTick >= 250)
                    {
                        lastMaintenanceTick = nowTick;
                        DismissModalWindows(Find.CurrentMap);
                        AutoApprovePendingOverlayRequests(nowTick);
                    }

                    if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
                    {
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                    }

                    yield return null;
                }

                DismissModalWindows(Find.CurrentMap);
                AutoApprovePendingOverlayRequests(Find.TickManager.TicksGame);
            }

            private void AutoApprovePendingOverlayRequests(int currentTick)
            {
                try
                {
                    var pending = RimMindAPI.GetPendingRequests();
                    if (pending == null || pending.Count == 0) return;

                    var snapshot = pending.ToList();
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        var req = snapshot[i];
                        if (req == null) continue;

                        string approveChoice = (req.options != null && req.options.Length > 0 && !string.IsNullOrWhiteSpace(req.options[0]))
                            ? req.options[0]
                            : "Approve";

                        if (RequestOverlay.Resolve(req, approveChoice))
                        {
                            _approvedOverlayItems.Add(new ApprovedOverlayItem
                            {
                                Source = req.source ?? string.Empty,
                                Title = req.title ?? string.Empty,
                                Description = req.description ?? string.Empty,
                                Choice = approveChoice,
                                Tick = currentTick
                            });
                            Log.Message($"[RimMind-Playthrough] Auto-approved RequestOverlay item (source={req.source}, choice={approveChoice}): {req.title}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough] AutoApprovePendingOverlayRequests notice: {ex.Message}");
                }
            }

            private PlaythroughDayReport CollectDayReport(int day, int dayStartTick, int dayEndTick, string dateStr)
            {
                var report = new PlaythroughDayReport
                {
                    DayNumber = day,
                    StartTick = dayStartTick,
                    EndTick = dayEndTick,
                    DateString = dateStr
                };

                var colonists = GetActiveSpawnedColonists();
                foreach (var p in colonists)
                {
                    var comp = CompPawnAgent.GetComp(p);
                    bool isActive = comp?.Agent?.IsActive == true;
                    string modeStr = comp?.Agent?.CurrentModeId.Value ?? "dormant";

                    report.Colonists.Add(new PlaythroughColonistSnapshot
                    {
                        Name = p.Name?.ToStringShort ?? "Colonist",
                        Mood = p.needs?.mood?.CurLevel ?? 0.5f,
                        CurrentJob = p.CurJobDef?.defName ?? "Idle",
                        HealthSummary = p.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%",
                        IsAgentActive = isActive,
                        AgentMode = modeStr
                    });
                }

                CollectTraceLogMetrics(report);
                CollectRealAgentActions(colonists, dayStartTick, dayEndTick, report);
                CollectRealDialogueEntries(dayStartTick, dayEndTick, report);
                CollectRealPersonalityThoughts(colonists, dayStartTick, dayEndTick, report);
                CollectRealAdvisorHistory(dayStartTick, dayEndTick, report);
                CollectRealMemoryCounts(colonists, report);

                return report;
            }

            private void CollectTraceLogMetrics(PlaythroughDayReport dayReport)
            {
                try
                {
                    var scope = RuntimeServiceHub.Shared.Capture();
                    var traceLog = scope.GetOptional<IAIRequestTraceLog>();
                    if (traceLog?.Entries == null) return;

                    int dayPromptTokens = 0;
                    int dayPrefixTokens = 0;

                    foreach (var entry in traceLog.Entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.RequestId)) continue;
                        if (entry.State != AIRequestTraceState.Completed && entry.State != AIRequestTraceState.Failed) continue;
                        if (!_processedTraceRequestIds.Add(entry.RequestId)) continue;

                        dayReport.LlmCallsCount++;
                        _totalLlmCalls++;

                        if (entry.State == AIRequestTraceState.Completed)
                        {
                            dayReport.SuccessfulLlmCallsCount++;
                            _successfulLlmCalls++;
                        }

                        int tokens = Math.Max(0, entry.TokensUsed);
                        dayReport.TotalTokensUsed += tokens;
                        _totalTokens += tokens;

                        string sourceKey = string.IsNullOrWhiteSpace(entry.Source) ? "Unknown" : entry.Source;
                        dayReport.LlmCallsBySource.TryGetValue(sourceKey, out int daySrcCount);
                        dayReport.LlmCallsBySource[sourceKey] = daySrcCount + 1;

                        _report.LlmCallsBySource.TryGetValue(sourceKey, out int totalSrcCount);
                        _report.LlmCallsBySource[sourceKey] = totalSrcCount + 1;

                        int promptTokens = Math.Max(0, entry.PromptTokens);
                        int cachedTokens = Math.Max(0, entry.CachedTokens);
                        if (promptTokens > 0)
                        {
                            if (cachedTokens > promptTokens)
                                cachedTokens = promptTokens;
                            dayPromptTokens += promptTokens;
                            _totalPromptTokens += promptTokens;
                            dayPrefixTokens += cachedTokens;
                            _totalPrefixTokens += cachedTokens;
                        }
                    }

                    dayReport.PrefixTokens = dayPrefixTokens;
                    dayReport.CacheHitRatio = dayPromptTokens > 0
                        ? Math.Round((double)dayPrefixTokens * 100.0 / dayPromptTokens, 1)
                        : 0.0;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectTraceLogMetrics notice: {ex.Message}");
                }
            }

            private static void CollectRealAgentActions(
                List<Pawn> colonists,
                int dayStartTick,
                int dayEndTick,
                PlaythroughDayReport dayReport)
            {
                try
                {
                    var actionsToday = new List<(string PawnName, BehaviorRecord Record)>();
                    var visitedPawnIds = new HashSet<int>();

                    void CollectFromPawn(Pawn? pawn)
                    {
                        if (pawn == null || pawn.thingIDNumber <= 0 || !visitedPawnIds.Add(pawn.thingIDNumber))
                            return;

                        var agent = CompPawnAgent.GetComp(pawn)?.Agent;
                        if (agent == null) return;

                        var history = agent.GetRecentHistory(50);
                        if (history == null) return;

                        string pawnName = pawn.Name?.ToStringShort ?? "Colonist";
                        for (int i = 0; i < history.Count; i++)
                        {
                            var rec = history[i];
                            if (rec != null && rec.Timestamp >= dayStartTick && rec.Timestamp <= dayEndTick)
                            {
                                actionsToday.Add((pawnName, rec));
                            }
                        }
                    }

                    foreach (var pawn in colonists)
                    {
                        CollectFromPawn(pawn);
                    }

                    // Also include colonists who died later during the day (corpses on loaded maps)
                    // so their executed actions are never dropped from the daily audit.
                    var maps = Find.Maps;
                    if (maps != null)
                    {
                        for (int m = 0; m < maps.Count; m++)
                        {
                            var corpses = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                            if (corpses == null) continue;
                            for (int c = 0; c < corpses.Count; c++)
                            {
                                if (corpses[c] is Corpse corpse && corpse.InnerPawn is Pawn inner && inner.IsColonist)
                                {
                                    CollectFromPawn(inner);
                                }
                            }
                        }
                    }

                    actionsToday.Sort((a, b) => a.Record.Timestamp.CompareTo(b.Record.Timestamp));
                    dayReport.AgentActionsExecutedCount = actionsToday.Count;

                    foreach (var item in actionsToday)
                    {
                        dayReport.AgentActions.Add(new PlaythroughAgentActionSummary
                        {
                            PawnName = item.PawnName,
                            Action = item.Record.Action ?? string.Empty,
                            Reason = item.Record.Reason ?? string.Empty,
                            Success = item.Record.Success,
                            ResultReason = item.Record.ResultReason ?? string.Empty,
                            Tick = item.Record.Timestamp
                        });
                    }

                    if (actionsToday.Count > 0)
                    {
                        var primary = actionsToday.LastOrDefault(a => a.Record.Success);
                        if (primary.Record == null)
                        {
                            primary = actionsToday[actionsToday.Count - 1];
                        }

                        int successCount = actionsToday.Count(a => a.Record.Success);
                        string toolsSummary = string.Join(
                            ", ",
                            actionsToday
                                .Select(a => $"{a.PawnName}:{a.Record.Action}({(a.Record.Success ? "OK" : "ERR")})")
                                .Take(8));

                        dayReport.AgentActionPawn = primary.PawnName;
                        dayReport.AgentActionTool = primary.Record.Action ?? string.Empty;
                        dayReport.AgentActionDetail =
                            $"{actionsToday.Count} action(s) executed ({successCount} succeeded): {toolsSummary}";
                        dayReport.AgentActionReason = !string.IsNullOrWhiteSpace(primary.Record.Reason)
                            ? primary.Record.Reason
                            : primary.Record.ResultReason ?? string.Empty;

                        dayReport.ColonyOperationName = $"PawnAgent:{dayReport.AgentActionTool}";
                        dayReport.ColonyOperationDetail = dayReport.AgentActionDetail;
                        dayReport.ColonyOperationSuccess = successCount > 0;
                    }
                    else
                    {
                        dayReport.AgentActionPawn = string.Empty;
                        dayReport.AgentActionTool = string.Empty;
                        dayReport.AgentActionDetail = "No PawnAgent action executed in this tick window";
                        dayReport.AgentActionReason = string.Empty;
                        dayReport.ColonyOperationName = "NativeAutonomy";
                        dayReport.ColonyOperationDetail = "No PawnAgent tool call recorded today";
                        dayReport.ColonyOperationSuccess = false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectRealAgentActions notice: {ex.Message}");
                }
            }

            private static void CollectRealDialogueEntries(int dayStartTick, int dayEndTick, PlaythroughDayReport dayReport)
            {
                try
                {
                    Type? serviceType = GenTypes.GetTypeInAnyAssembly("RimMind.Dialogue.Core.RimMindDialogueService");
                    if (serviceType == null) return;

                    MethodInfo? exportMethod = serviceType.GetMethod(
                        "ExportLogEntries",
                        BindingFlags.Public | BindingFlags.Static);
                    if (exportMethod == null) return;

                    if (exportMethod.Invoke(null, new object[] { 200 }) is not IEnumerable entries) return;

                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    object? latestEntry = null;
                    int latestTick = int.MinValue;
                    int countToday = 0;

                    foreach (object? entry in entries)
                    {
                        if (entry == null) continue;
                        var entryType = entry.GetType();
                        int tick = (entryType.GetField("tick", flags)?.GetValue(entry) as int?) ?? -1;
                        if (tick >= dayStartTick && tick <= dayEndTick)
                        {
                            countToday++;
                            if (tick >= latestTick)
                            {
                                latestTick = tick;
                                latestEntry = entry;
                            }
                        }
                    }

                    dayReport.DialogueCount = countToday;
                    if (latestEntry != null)
                    {
                        var t = latestEntry.GetType();
                        dayReport.DialogueSpeaker = (t.GetField("initiatorName", flags)?.GetValue(latestEntry) as string) ?? string.Empty;
                        dayReport.DialogueListener = (t.GetField("recipientName", flags)?.GetValue(latestEntry) as string) ?? "Monologue";
                        dayReport.DialogueSpeech = (t.GetField("reply", flags)?.GetValue(latestEntry) as string) ?? string.Empty;
                        dayReport.DialogueThoughtTag = (t.GetField("thoughtTag", flags)?.GetValue(latestEntry) as string) ?? "NONE";
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectRealDialogueEntries notice: {ex.Message}");
                }
            }

            private static void CollectRealPersonalityThoughts(
                List<Pawn> colonists,
                int dayStartTick,
                int dayEndTick,
                PlaythroughDayReport dayReport)
            {
                try
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                    // 1. Inspect colonists' active Thought_AIPersonality memories
                    foreach (var pawn in colonists)
                    {
                        var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
                        if (memories == null) continue;

                        for (int i = memories.Count - 1; i >= 0; i--)
                        {
                            var m = memories[i];
                            if (m == null || m.GetType().Name != "Thought_AIPersonality") continue;

                            var mType = m.GetType();
                            string aiLabel = (mType.GetField("aiLabel", flags)?.GetValue(m) as string) ?? m.LabelCap;
                            string aiDesc = (mType.GetField("aiDescription", flags)?.GetValue(m) as string) ?? m.Description;
                            int aiIntensity = (mType.GetField("aiIntensity", flags)?.GetValue(m) as int?) ?? 0;

                            if (!string.IsNullOrWhiteSpace(aiLabel) || !string.IsNullOrWhiteSpace(aiDesc))
                            {
                                dayReport.MorningPawnName = pawn.Name?.ToStringShort ?? "Colonist";
                                dayReport.MorningThought = $"{aiLabel} (intensity {aiIntensity}): {aiDesc}";
                                return;
                            }
                        }
                    }

                    // 2. Inspect AIPersonalityWorldComponent PersonalityProfile.aiNarrative
                    object? worldComp = Find.World?.components?.FirstOrDefault(c => c != null && c.GetType().Name == "AIPersonalityWorldComponent");
                    if (worldComp != null)
                    {
                        MethodInfo? tryGetOrCreate = worldComp.GetType().GetMethod("GetOrCreate", flags);
                        if (tryGetOrCreate != null)
                        {
                            foreach (var pawn in colonists)
                            {
                                object? profile = tryGetOrCreate.Invoke(worldComp, new object[] { pawn });
                                if (profile == null) continue;

                                var pType = profile.GetType();
                                string narrative = (pType.GetField("aiNarrative", flags)?.GetValue(profile) as string) ?? string.Empty;
                                string description = (pType.GetField("description", flags)?.GetValue(profile) as string) ?? string.Empty;
                                int lastTick = (pType.GetField("lastNarrativeUpdateTick", flags)?.GetValue(profile) as int?) ?? 0;

                                if ((!string.IsNullOrWhiteSpace(narrative) || !string.IsNullOrWhiteSpace(description)) &&
                                    (lastTick >= dayStartTick && lastTick <= dayEndTick || string.IsNullOrWhiteSpace(dayReport.MorningThought)))
                                {
                                    dayReport.MorningPawnName = pawn.Name?.ToStringShort ?? "Colonist";
                                    dayReport.MorningThought = !string.IsNullOrWhiteSpace(narrative) ? narrative : description;
                                    if (lastTick >= dayStartTick && lastTick <= dayEndTick)
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectRealPersonalityThoughts notice: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(dayReport.MorningThought))
                {
                    dayReport.MorningPawnName = colonists.FirstOrDefault()?.Name?.ToStringShort ?? "Colonist";
                    dayReport.MorningThought = "No Personality thought triggered today";
                }
            }

            private void CollectRealAdvisorHistory(int dayStartTick, int dayEndTick, PlaythroughDayReport dayReport)
            {
                try
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    object? storeComp = Find.World?.components?.FirstOrDefault(c => c != null && c.GetType().Name == "AdvisorHistoryStore");

                    if (storeComp != null)
                    {
                        PropertyInfo? globalLogProp = storeComp.GetType().GetProperty("GlobalLog", flags);
                        if (globalLogProp?.GetValue(storeComp) is IEnumerable globalLog)
                        {
                            var todayRecords = new List<(string Action, string Reason, string Result, int Tick)>();
                            foreach (object? rec in globalLog)
                            {
                                if (rec == null) continue;
                                var rType = rec.GetType();
                                int tick = (rType.GetField("tick", flags)?.GetValue(rec) as int?) ?? -1;
                                if (tick >= dayStartTick && tick <= dayEndTick)
                                {
                                    string action = (rType.GetField("action", flags)?.GetValue(rec) as string) ?? string.Empty;
                                    string reason = (rType.GetField("reason", flags)?.GetValue(rec) as string) ?? string.Empty;
                                    string result = (rType.GetField("result", flags)?.GetValue(rec) as string) ?? string.Empty;
                                    todayRecords.Add((action, reason, result, tick));
                                }
                            }

                            if (todayRecords.Count > 0)
                            {
                                var latest = todayRecords[todayRecords.Count - 1];
                                dayReport.AdvisorProposal = $"[{latest.Action}] {latest.Reason} -> {latest.Result}";
                                dayReport.AdvisorApproved = true;
                                return;
                            }
                        }
                    }

                    var approvedToday = _approvedOverlayItems
                        .Where(x => x.Tick >= dayStartTick && x.Tick <= dayEndTick &&
                                    (string.IsNullOrWhiteSpace(x.Source) || x.Source.IndexOf("advisor", StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();

                    if (approvedToday.Count > 0)
                    {
                        var lastApproved = approvedToday[approvedToday.Count - 1];
                        dayReport.AdvisorProposal = $"[{lastApproved.Source}] {lastApproved.Title}: {lastApproved.Description}";
                        dayReport.AdvisorApproved = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectRealAdvisorHistory notice: {ex.Message}");
                }

                dayReport.AdvisorProposal = "No Advisor proposal triggered today";
                dayReport.AdvisorApproved = false;
            }

            private static void CollectRealMemoryCounts(List<Pawn> colonists, PlaythroughDayReport dayReport)
            {
                try
                {
                    object? comp = Find.World?.components?.FirstOrDefault(c => c != null && c.GetType().Name == "RimMindMemoryWorldComponent")
                        ?? (object?)Current.Game?.components?.FirstOrDefault(c => c != null && c.GetType().Name == "RimMindMemoryWorldComponent");
                    if (comp == null) return;

                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var getStoreMethod = comp.GetType().GetMethod("GetPawnStore", flags)
                        ?? comp.GetType().GetMethod("GetOrCreatePawnStore", flags);

                    int activeCount = 0;
                    int archiveCount = 0;
                    int darkCount = 0;

                    if (getStoreMethod != null)
                    {
                        foreach (var pawn in colonists)
                        {
                            var store = getStoreMethod.Invoke(comp, new object[] { pawn });
                            if (store == null) continue;

                            var storeType = store.GetType();
                            if (storeType.GetField("active", flags)?.GetValue(store) is IList activeList)
                                activeCount += activeList.Count;
                            if (storeType.GetField("archive", flags)?.GetValue(store) is IList archiveList)
                                archiveCount += archiveList.Count;
                            if (storeType.GetField("dark", flags)?.GetValue(store) is IList darkList)
                                darkCount += darkList.Count;
                        }
                    }

                    var narratorStore = comp.GetType().GetProperty("NarratorStore", flags)?.GetValue(comp);
                    if (narratorStore != null)
                    {
                        var nType = narratorStore.GetType();
                        if (nType.GetField("active", flags)?.GetValue(narratorStore) is IList nActive)
                            activeCount += nActive.Count;
                        if (nType.GetField("archive", flags)?.GetValue(narratorStore) is IList nArchive)
                            archiveCount += nArchive.Count;
                        if (nType.GetField("dark", flags)?.GetValue(narratorStore) is IList nDark)
                            darkCount += nDark.Count;
                    }

                    dayReport.WorkingMemoryCount = activeCount;
                    dayReport.EpisodicMemoryCount = archiveCount;
                    dayReport.ColonyFactCount = darkCount;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {dayReport.DayNumber}] CollectRealMemoryCounts notice: {ex.Message}");
                }
            }

            private IEnumerator CaptureDayScreenshotRoutine(int day, PlaythroughDayReport report)
            {
                DismissModalWindows(Find.CurrentMap);
                FrameSurroundingBuildings(Find.CurrentMap);
                yield return new WaitForEndOfFrame();

                string fileName = $"day-{day:D2}.png";
                string filePath = Path.Combine(_outputDir, fileName);

                try
                {
                    Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                    if (screenshot != null)
                    {
                        byte[] bytes = screenshot.EncodeToPNG();
                        File.WriteAllBytes(filePath, bytes);
                        UnityEngine.Object.Destroy(screenshot);

                        MirrorFileIfDistinct(filePath, Path.Combine(_legacyRunOutputDir, fileName));
                        MirrorFileIfDistinct(filePath, Path.Combine(_bridgeDir, fileName));
                    }
                    else
                    {
                        ScreenCapture.CaptureScreenshot(filePath);
                    }

                    report.ScreenshotPath = filePath;
                    Log.Message($"[RimMind-Playthrough][Day {day}] Saved screenshot to: {filePath}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to capture screenshot: {ex.Message}");
                }
            }

            private static void FrameSurroundingBuildings(Map? map)
            {
                if (map == null || Find.CameraDriver == null) return;
                try
                {
                    var focusCells = new List<IntVec3>();

                    var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map);
                    foreach (var p in colonists)
                    {
                        if (p != null && p.Spawned && p.Position.IsValid)
                            focusCells.Add(p.Position);
                    }

                    var buildings = map.listerBuildings?.allBuildingsColonist;
                    if (buildings != null)
                    {
                        for (int i = 0; i < buildings.Count; i++)
                        {
                            var b = buildings[i];
                            if (b != null && b.Spawned && b.Position.IsValid)
                                focusCells.Add(b.Position);
                        }
                    }

                    var blueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint);
                    if (blueprints != null)
                    {
                        for (int i = 0; i < blueprints.Count; i++)
                        {
                            var bp = blueprints[i];
                            if (bp != null && bp.Spawned && bp.Faction == Faction.OfPlayer && bp.Position.IsValid)
                                focusCells.Add(bp.Position);
                        }
                    }

                    if (focusCells.Count == 0)
                    {
                        Find.CameraDriver.JumpToCurrentMapLoc(map.Center);
                        return;
                    }

                    long sumX = 0;
                    long sumZ = 0;
                    for (int i = 0; i < focusCells.Count; i++)
                    {
                        sumX += focusCells[i].x;
                        sumZ += focusCells[i].z;
                    }

                    var centroid = new IntVec3(
                        (int)(sumX / focusCells.Count),
                        0,
                        (int)(sumZ / focusCells.Count));

                    if (!centroid.InBounds(map))
                        centroid = map.Center;

                    Find.CameraDriver.JumpToCurrentMapLoc(centroid);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough] FrameSurroundingBuildings notice: {ex.Message}");
                }
            }

            private void ExportDailyStateJson(int day, PlaythroughDayReport report, Map? map)
            {
                try
                {
                    int woodCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
                    int steelCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
                    int mealsCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.MealSimple)
                        + MapResourceCounter.GetAvailableCount(map, ThingDefOf.MealFine)
                        + MapResourceCounter.GetAvailableCount(map, ThingDefOf.MealSurvivalPack);
                    float totalNutrition = MapResourceCounter.GetAvailableFoodNutrition(map);
                    int blueprintsCount = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                    int framesCount = map?.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
                    int buildingsCount = map?.listerBuildings?.allBuildingsColonist?.Count ?? 0;

                    var statePayload = new
                    {
                        day = day,
                        date = report.DateString,
                        startTick = report.StartTick,
                        endTick = report.EndTick,
                        colonists = PlaythroughPlayerSimulation.BuildColonistVitals(map, report.Colonists),
                        llmCalls = report.LlmCallsCount,
                        successfulLlmCalls = report.SuccessfulLlmCallsCount,
                        tokensUsed = report.TotalTokensUsed,
                        prefixTokens = report.PrefixTokens,
                        cacheHitRatio = report.CacheHitRatio,
                        llmCallsBySource = report.LlmCallsBySource,
                        agentActionsExecuted = report.AgentActionsExecutedCount,
                        agentActions = report.AgentActions,
                        dialogue = new
                        {
                            count = report.DialogueCount,
                            speaker = report.DialogueSpeaker,
                            listener = report.DialogueListener,
                            speech = report.DialogueSpeech,
                            thoughtTag = report.DialogueThoughtTag
                        },
                        personality = new
                        {
                            pawn = report.MorningPawnName,
                            thought = report.MorningThought
                        },
                        advisor = new
                        {
                            proposal = report.AdvisorProposal,
                            approved = report.AdvisorApproved
                        },
                        memory = new
                        {
                            active = report.ActiveMemories,
                            archive = report.ArchiveMemories,
                            dark = report.DarkMemories
                        },
                        mapStats = new
                        {
                            weather = map?.weatherManager?.curWeather?.label ?? "Clear",
                            outdoorTemp = Mathf.RoundToInt(map?.mapTemperature?.OutdoorTemp ?? 21f),
                            woodCount = woodCount,
                            steelCount = steelCount,
                            mealsCount = mealsCount,
                            foodNutrition = Mathf.Round(totalNutrition * 10f) / 10f,
                            unstockpiledFoodNutrition = Mathf.Round(PlaythroughPlayerSimulation.CountUnstockpiledNutrition(map) * 10f) / 10f,
                            totalAvailableNutrition = Mathf.Round(totalNutrition * 10f) / 10f,
                            medicineCount = PlaythroughPlayerSimulation.CountMedicine(map),
                            activeAlerts = PlaythroughPlayerSimulation.CollectActiveAlerts(),
                            openWindows = PlaythroughPlayerSimulation.CollectOpenWindows(),
                            colonistBuildingsCount = buildingsCount,
                            pendingConstructionCount = blueprintsCount + framesCount
                        },
                        screenshot = Path.GetFileName(report.ScreenshotPath)
                    };

                    string json = JsonConvert.SerializeObject(statePayload, Formatting.Indented);
                    string stateFilePath = Path.Combine(_outputDir, $"day-{day:D2}-state.json");
                    File.WriteAllText(stateFilePath, json, Encoding.UTF8);

                    MirrorFileIfDistinct(stateFilePath, Path.Combine(_legacyRunOutputDir, $"day-{day:D2}-state.json"));
                    MirrorFileIfDistinct(stateFilePath, Path.Combine(_bridgeDir, $"state_day_{day:D2}.json"));
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {day}] ExportDailyStateJson notice: {ex.Message}");
                }
            }

            private void SaveReportCheckpoint()
            {
                try
                {
                    string json = JsonConvert.SerializeObject(_report, Formatting.Indented);
                    string primaryName = $"playthrough-{_totalDays}days-report.json";
                    string primaryPath = Path.Combine(_outputDir, primaryName);
                    File.WriteAllText(primaryPath, json, Encoding.UTF8);
                    MirrorFileIfDistinct(primaryPath, Path.Combine(_legacyRunOutputDir, primaryName));

                    if (_totalDays != 10)
                    {
                        string compatPath = Path.Combine(_outputDir, "playthrough-10days-report.json");
                        File.WriteAllText(compatPath, json, Encoding.UTF8);
                        MirrorFileIfDistinct(compatPath, Path.Combine(_legacyRunOutputDir, "playthrough-10days-report.json"));
                    }

                    GeneratePlaythroughChronicle();
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimMind-Playthrough] Failed to save report checkpoint: " + ex.Message);
                }
            }

            private void GeneratePlaythroughChronicle()
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"# RimMind {_totalDays}-Day Genuine In-Game AI Playthrough Chronicle");
                    sb.AppendLine();
                    sb.AppendLine($"> **Run ID**: `{_report.RunId}`");
                    sb.AppendLine($"> **Endpoint / Model**: `{_report.Endpoint}` (`{_report.Model}`)");
                    sb.AppendLine($"> **Elapsed**: {_report.TotalDurationMs / 1000f:F1}s | **Total Ticks**: {_report.TotalTicksElapsed:N0} ({_report.TotalDaysCompleted}/{_totalDays} days)");
                    sb.AppendLine($"> **Real LLM Calls**: {_report.TotalLiveLlmRequests} ({_report.SuccessfulLiveLlmRequests} succeeded) | **Total Tokens**: {_report.TotalTokensConsumed:N0} | **Prefix Tokens**: {_report.TotalPrefixTokens:N0} (`{_report.CacheHitRatio:F1}%` cache hit)");
                    if (_report.LlmCallsBySource.Count > 0)
                    {
                        string bySource = string.Join(", ", _report.LlmCallsBySource.Select(kv => $"`{kv.Key}`: {kv.Value}"));
                        sb.AppendLine($"> **Calls By Subsystem**: {bySource}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();

                    foreach (var d in _report.Days)
                    {
                        sb.AppendLine($"## Day {d.DayNumber} · {d.DateString} (Ticks {d.StartTick}–{d.EndTick})");
                        sb.AppendLine();
                        sb.AppendLine($"- **LLM Requests Today**: {d.LlmCallsCount} ({d.SuccessfulLlmCallsCount} succeeded) | **Tokens**: {d.TotalTokensUsed} (Prefix: {d.PrefixTokens}, CacheHit: `{d.CacheHitRatio:F1}%`) | **Duration**: {d.DayComputeDurationMs}ms");
                        sb.AppendLine($"- **Personality ({d.MorningPawnName})**: {d.MorningThought}");
                        sb.AppendLine($"- **PawnAgent Actions ({d.AgentActionsExecutedCount})**: {d.AgentActionDetail}");
                        if (!string.IsNullOrWhiteSpace(d.DialogueSpeech))
                        {
                            sb.AppendLine($"- **Dialogue ({d.DialogueCount})**: **{d.DialogueSpeaker}** → **{d.DialogueListener}**: *\"{d.DialogueSpeech}\"* (`{d.DialogueThoughtTag}`)");
                        }
                        else
                        {
                            sb.AppendLine("- **Dialogue**: No dialogue recorded today");
                        }
                        sb.AppendLine($"- **Advisor**: {d.AdvisorProposal} (Approved: {d.AdvisorApproved})");
                        sb.AppendLine($"- **Memory Store**: Active={d.ActiveMemories}, Archive={d.ArchiveMemories}, Dark={d.DarkMemories}");
                        if (!string.IsNullOrWhiteSpace(d.ScreenshotPath))
                        {
                            sb.AppendLine($"- **Screenshot**: `{Path.GetFileName(d.ScreenshotPath)}`");
                        }
                        sb.AppendLine();
                    }

                    string content = sb.ToString();
                    string primaryName = $"playthrough-{_totalDays}days-chronicle.md";
                    string primaryPath = Path.Combine(_outputDir, primaryName);
                    File.WriteAllText(primaryPath, content, Encoding.UTF8);
                    MirrorFileIfDistinct(primaryPath, Path.Combine(_legacyRunOutputDir, primaryName));

                    if (_totalDays != 10)
                    {
                        string compatPath = Path.Combine(_outputDir, "playthrough-10days-chronicle.md");
                        File.WriteAllText(compatPath, content, Encoding.UTF8);
                        MirrorFileIfDistinct(compatPath, Path.Combine(_legacyRunOutputDir, "playthrough-10days-chronicle.md"));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimMind-Playthrough] Failed to generate chronicle: " + ex.Message);
                }
            }

            private static void MirrorFileIfDistinct(string sourcePath, string targetPath)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath)) return;
                    if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                        return;

                    string? dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.Copy(sourcePath, targetPath, overwrite: true);
                }
                catch { }
            }

            private static void DismissExcessLetters()
            {
                try
                {
                    var letters = Find.LetterStack?.LettersListForReading;
                    if (letters == null) return;

                    int safetyLimit = 200;
                    while (letters.Count > 5 && safetyLimit-- > 0)
                    {
                        Find.LetterStack!.RemoveLetter(letters[0]);
                    }
                }
                catch { }
            }

            private static void DismissModalWindows(Map? map)
            {
                try
                {
                    DismissExcessLetters();

                    if (Find.FactionManager?.OfPlayer != null && !Find.FactionManager.OfPlayer.HasName)
                    {
                        NamePlayerFactionDialogUtility.Named("RimMind Federation");
                    }

                    if (map?.info?.parent is RimWorld.Planet.Settlement playerSettlement)
                    {
                        if (!playerSettlement.HasName || !playerSettlement.namedByPlayer)
                        {
                            NamePlayerSettlementDialogUtility.Named(playerSettlement, playerSettlement.HasName ? playerSettlement.Name : "RimMind Prime");
                        }
                    }

                    if (Find.WindowStack?.Windows == null) return;

                    LudeonTK.EditWindow_Log.wantsToOpen = false;

                    var windows = Find.WindowStack.Windows.ToList();
                    for (int i = 0; i < windows.Count; i++)
                    {
                        var w = windows[i];
                        if (w == null) continue;
                        string typeName = w.GetType().Name;
                        if (typeName.IndexOf("Advisor", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        if (w is Dialog_GiveName ||
                            w is Dialog_MessageBox ||
                            w is Dialog_NodeTree ||
                            w is FloatMenu ||
                            w is LudeonTK.EditWindow_Log ||
                            w.absorbInputAroundWindow ||
                            typeName.IndexOf("EditWindow_Log", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("GiveName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("Immediate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("NodeTree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("MessageBox", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Find.WindowStack.TryRemove(w, doCloseSound: false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough] DismissModalWindows notice: {ex.Message}");
                }
            }

            private void OnDestroy()
            {
                RestorePlayerSettings();
                if (_activeHost == this)
                {
                    _activeHost = null;
                }
            }
        }
    }
}
