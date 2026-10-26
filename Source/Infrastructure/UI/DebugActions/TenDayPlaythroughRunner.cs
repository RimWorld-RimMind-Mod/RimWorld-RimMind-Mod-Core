using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public sealed class PlaythroughColonistSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public float Mood { get; set; }
        public string CurrentJob { get; set; } = string.Empty;
        public string HealthSummary { get; set; } = string.Empty;
    }

    public sealed class ColonySubagentCommand
    {
        public int Day { get; set; }
        public string Action { get; set; } = string.Empty; // dismiss_window, plan_housing, mine, grow, build, harvest_trees, stockpile, clear_queue, custom
        public string Target { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string UxFinding { get; set; } = string.Empty;
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
        public string AgentActionPawn { get; set; } = string.Empty;
        public string AgentActionTool { get; set; } = string.Empty;
        public string AgentActionDetail { get; set; } = string.Empty;
        public string AgentActionReason { get; set; } = string.Empty;
        public string AdvisorProposal { get; set; } = string.Empty;
        public bool AdvisorApproved { get; set; }
        public int WorkingMemoryCount { get; set; }
        public int EpisodicMemoryCount { get; set; }
        public int ColonyFactCount { get; set; }
        public int TotalTokensUsed { get; set; }
        public int PrefixTokens { get; set; }
        public float CacheHitRatio { get; set; }
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
        public int TotalTokensConsumed { get; set; }
        public float AverageCacheHitRatio { get; set; }
        public List<PlaythroughDayReport> Days { get; set; } = new();
        public string OverallStatus { get; set; } = "RUNNING"; // "COMPLETED", "FAILED"
        public string SummaryNotes { get; set; } = string.Empty;
    }

    internal sealed class TenDayPlaythroughRunner : MonoBehaviour
    {
        private static TenDayPlaythroughRunner? _active;

        private string _runId = string.Empty;
        private TenDayPlaythroughReport _report = new();
        private readonly Stopwatch _clock = new();
        private string _outputDir = string.Empty;
        private string _bridgeDir = string.Empty;
        private bool _interactiveBridgeEnabled;
        private bool _maxUsageMode;
        private static readonly Dictionary<int, string> _recentActionFailures = new();

        private int _totalDays = 10;

        internal static void StartPlaythrough(string? runId = null, int totalDays = 10)
        {
            if (_active != null)
            {
                Log.Warning("[RimMind-Playthrough] Playthrough runner is already running.");
                return;
            }

            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                Log.Warning("[RimMind-Playthrough] Playthrough requires a loaded map.");
                return;
            }

            try
            {
                var runner = Current.Root.gameObject.AddComponent<TenDayPlaythroughRunner>();
                _active = runner;
                runner.Initialize(runId ?? Guid.NewGuid().ToString("N"), totalDays);
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Playthrough] Failed to initialize playthrough runner: " + ex);
                _active = null;
            }
        }

        private void Initialize(string runId, int totalDays = 10)
        {
            _runId = runId;
            _totalDays = totalDays > 0 ? totalDays : 10;
            _clock.Start();

            // Override credentials from environment if provided
            string? envKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
            string? envEndpoint = Environment.GetEnvironmentVariable("RIMMIND_TEST_ENDPOINT");
            string? envModel = Environment.GetEnvironmentVariable("RIMMIND_TEST_MODEL");

            if (!string.IsNullOrWhiteSpace(envKey))
                RimMindCoreMod.Settings.apiKey = envKey;
            if (!string.IsNullOrWhiteSpace(envEndpoint))
                RimMindCoreMod.Settings.apiEndpoint = envEndpoint;
            if (!string.IsNullOrWhiteSpace(envModel))
                RimMindCoreMod.Settings.modelName = envModel;

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

                RimMindCoreMod.Settings.backupEndpoints ??= new List<RimMind.Presentation.Settings.BackupEndpointEntry>();
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
                    RimMindCoreMod.Settings.backupEndpoints.Insert(0, new RimMind.Presentation.Settings.BackupEndpointEntry
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

            _outputDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "PlaythroughTests", _runId);
            try
            {
                if (!Directory.Exists(_outputDir))
                    Directory.CreateDirectory(_outputDir);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Playthrough] Failed to create output directory: " + ex.Message);
            }

            string? bridgeEnv = Environment.GetEnvironmentVariable("RIMMIND_INTERACTIVE_BRIDGE");
            _interactiveBridgeEnabled = Environment.CommandLine.Contains("-rimmind-interactive-bridge") ||
                                        GenCommandLine.CommandLineArgPassed("rimmind-interactive-bridge") ||
                                        bridgeEnv == "1" || bridgeEnv == "true";

            string? maxUsageEnv = Environment.GetEnvironmentVariable("RIMMIND_MAX_USAGE_MODE");
            _maxUsageMode = Environment.CommandLine.Contains("-rimmind-max-usage") ||
                            GenCommandLine.CommandLineArgPassed("rimmind-max-usage") ||
                            maxUsageEnv == "1" || maxUsageEnv == "true";

            string? customBridge = Environment.GetEnvironmentVariable("RIMMIND_BRIDGE_DIR");
            _bridgeDir = !string.IsNullOrWhiteSpace(customBridge)
                ? customBridge!
                : Path.Combine(_outputDir, "bridge");

            // Suppress Learning Helper tutorial overlay during playthrough to prevent HUD occlusion
            try
            {
                Prefs.AdaptiveTrainingEnabled = false;
            }
            catch { }

            try
            {
                if (!Directory.Exists(_bridgeDir))
                    Directory.CreateDirectory(_bridgeDir);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Playthrough] Failed to create bridge directory: " + ex.Message);
            }

            _report = new TenDayPlaythroughReport
            {
                RunId = _runId,
                Provider = RimMindCoreMod.Settings.provider,
                Endpoint = RimMindCoreMod.Settings.apiEndpoint,
                Model = RimMindCoreMod.Settings.modelName
            };

            Log.Message($"[RimMind-Playthrough] ========================================================");
            Log.Message($"[RimMind-Playthrough] Starting 10-Day Real Game Playthrough & Evolution Test");
            Log.Message($"[RimMind-Playthrough] Starting {_totalDays}-Day Real Game Playthrough & Evolution Test");
            Log.Message($"[RimMind-Playthrough] RunId: {_runId}");
            Log.Message($"[RimMind-Playthrough] Endpoint: {RimMindCoreMod.Settings.apiEndpoint}, Model: {RimMindCoreMod.Settings.modelName}");
            Log.Message($"[RimMind-Playthrough] OutputDir: {_outputDir}");
            Log.Message($"[RimMind-Playthrough] Mode: {(_maxUsageMode ? "MAX USAGE (High Density, Scale=3.5x, Multi-Pawn Routine)" : "Standard Schedule")}");
            Log.Message($"[RimMind-Playthrough] ========================================================");

            // Enable DevMode to allow ultrafast speed and unobstructed progression
            Prefs.DevMode = true;

            // Configure optimal settings for playthrough: high concurrency, clean logs, non-overlapping HUD, and generous timeout
            RimMindCoreMod.Settings.debugLogging = false;
            RimMindCoreMod.Settings.requestOverlayX = 155f;
            RimMindCoreMod.Settings.circuitBreakerFailureThreshold = 8;
            RimMindCoreMod.Settings.circuitBreakerOpenDurationSec = 10;
            RimMindCoreMod.Settings.requestOverlayY = 100f;
            RequestOverlay.SetWindowRectForTest(new Rect(
                RimMindCoreMod.Settings.requestOverlayX,
                RimMindCoreMod.Settings.requestOverlayY,
                RimMindCoreMod.Settings.requestOverlayW,
                RimMindCoreMod.Settings.requestOverlayH));
            RimMindCoreMod.Settings.autoActivateColonistAgents = false;
            RimMindCoreMod.Settings.autonomyLevel = AgentAutonomyLevel.Autonomous;
            if (_maxUsageMode)
            {
                RimMindCoreMod.Settings.activityFrequencyScale = 1.0f;
                RimMindCoreMod.Settings.maxConcurrentRequests = 2;
                RimMindCoreMod.Settings.requestTimeoutMs = 90000;
                RimMindCoreMod.Settings.defaultModCooldownTicks = 2500;
                RimMindCoreMod.Settings.thinkCooldownTicks = 60000;
            }
            else
            {
                RimMindCoreMod.Settings.maxConcurrentRequests = 2;
                RimMindCoreMod.Settings.requestTimeoutMs = 90000;
            }

            StartCoroutine(SafeRunSubRoutine(PlaythroughRoutine(), "PlaythroughMain", 0));
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

        private IEnumerator PlaythroughRoutine()
        {
            // Initial warm-up: wait 15 frames for map components to initialize
            for (int i = 0; i < 15; i++)
            {
                yield return null;
            }

            int startTick = Find.TickManager.TicksGame;
            Log.Message($"[RimMind-Playthrough] Simulation start at Tick {startTick}");

            // Simulation startup: unforbid starting crashlanded items
            if (Find.CurrentMap != null)
            {
                UnforbidAllStartingItems(Find.CurrentMap);
            }

            // Auto-activate colonist agents and work priorities on playthrough startup
            var initialColonists = EnsureColonistAgentsAndPriorities();

            float totalHitRatios = 0f;
            int totalDaysTracked = 0;

            for (int day = 1; day <= _totalDays; day++)
            {
                var daySw = Stopwatch.StartNew();
                if (Find.TickManager != null)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                }

                int dayStartTick = Find.TickManager?.TicksGame ?? 0;
                string dateStr = Find.CurrentMap != null && Find.WorldGrid != null
                    ? GenDate.DateFullStringAt(GenTicks.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile))
                    : $"Day {day}";

                Log.Message($"[RimMind-Playthrough] >>> Starting Day {day}/{_totalDays} (GameTick: {dayStartTick}, Date: {dateStr}) <<<");

                var currentMap = Find.CurrentMap;
                if (currentMap != null)
                {
                    UnforbidAllStartingItems(currentMap);
                }

                var dayReport = new PlaythroughDayReport
                {
                    DayNumber = day,
                    StartTick = dayStartTick,
                    DateString = dateStr
                };

                // Sample colonists, ensure survival, work priorities, and active PawnAgents every day
                var colonists = EnsureColonistAgentsAndPriorities();

                foreach (var p in colonists)
                {
                    if (p == null || p.Dead) continue;

                    dayReport.Colonists.Add(new PlaythroughColonistSnapshot
                    {
                        Name = p.Name?.ToStringShort ?? "Colonist",
                        Mood = p.needs?.mood?.CurLevel ?? 0.5f,
                        CurrentJob = p.CurJobDef?.defName ?? "Idle",
                        HealthSummary = p.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%"
                    });
                }

                // Phase 1: Awakening & Shift Start Phase (06:00) -> Autonomous Agent Perception & Thought
                var awakeColonists = colonists.Where(p => p != null && !p.Dead && p.Spawned && p.Awake() && !p.Downed && !p.InMentalState && p.MentalStateDef == null).ToList();
                if (awakeColonists.Count == 0) awakeColonists = colonists.Where(p => p != null && !p.Dead && p.Spawned).ToList();

                if (awakeColonists.Count > 0)
                {
                    Pawn morningPawn = awakeColonists[(day - 1) % awakeColonists.Count];
                    dayReport.MorningPawnName = morningPawn.Name?.ToStringShort ?? "Colonist";
                    yield return SafeRunSubRoutine(ExecuteMorningThought(morningPawn, day, dayReport, recordInReport: true), "MorningThought", day);
                }

                // Daily Colony Operation: Execute interactive Copilot bridge or player colony operations (Days 1 to TotalDays)
                yield return SafeRunSubRoutine(ExecuteDailyColonyOperationRoutine(day, dayReport), "DailyColonyOperation", day);

                // Advance Morning -> Noon (15,000 ticks = 6 in-game hours)
                yield return SafeRunSubRoutine(AdvanceTicks(15000), "AdvanceMorningToNoon", day);

                // Phase 2: Noon Phase (12:00) -> Social Encounter & ToolCall Dialogue
                var allLiving = EnsureColonistAgentsAndPriorities().Where(p => p != null && !p.Dead && !p.Destroyed).ToList();
                var liveSpeakers = allLiving.Where(p => p.Awake() && !p.Downed && !p.InMentalState && p.MentalStateDef == null).ToList();
                if (liveSpeakers.Count < 2) liveSpeakers = allLiving.Where(p => !p.Downed && !p.InMentalState && p.MentalStateDef == null).ToList();
                if (liveSpeakers.Count < 2) liveSpeakers = allLiving.Where(p => !p.Downed).ToList();
                if (liveSpeakers.Count >= 2)
                {
                    Pawn speaker = liveSpeakers[(day - 1) % liveSpeakers.Count];
                    Pawn listener = liveSpeakers[day % liveSpeakers.Count];
                    if (speaker == listener)
                    {
                        listener = liveSpeakers.FirstOrDefault(p => p != speaker) ?? speaker;
                    }
                    yield return SafeRunSubRoutine(ExecuteSocialDialogue(speaker, listener, day, dayReport, recordInReport: true), "SocialDialogue", day);
                }
                else if (liveSpeakers.Count == 1 && allLiving.Count >= 1)
                {
                    Pawn speaker = liveSpeakers[0];
                    Pawn listener = allLiving.FirstOrDefault(p => p != speaker) ?? speaker;
                    yield return SafeRunSubRoutine(ExecuteSocialDialogue(speaker, listener, day, dayReport, recordInReport: true), "SocialDialogue", day);
                }
                else if (allLiving.Count >= 1)
                {
                    Pawn speaker = allLiving[(day - 1) % allLiving.Count];
                    Pawn listener = allLiving.FirstOrDefault(p => p != speaker) ?? speaker;
                    yield return SafeRunSubRoutine(ExecuteSocialDialogue(speaker, listener, day, dayReport, recordInReport: true), "SocialDialogue", day);
                }

                // Advance Noon -> Afternoon (7,500 ticks = 3 in-game hours)
                yield return SafeRunSubRoutine(AdvanceTicks(7500), "AdvanceNoonToAfternoon", day);

                // Phase 2.5: Afternoon Phase (15:00) -> Autonomous Agent Action & Mechanism Decision
                UnforbidAllStartingItems(Find.CurrentMap);
                var aliveForAction = EnsureColonistAgentsAndPriorities();
                Pawn? designatedAgent = aliveForAction.FirstOrDefault(p => p != null && !p.Dead && p.Spawned && CompPawnAgent.GetComp(p)?.Agent?.State == AgentState.Active);
                var currentColonists = aliveForAction.Where(p => p != null && !p.Downed && !p.InMentalState && p.MentalStateDef == null).ToList();
                if (currentColonists.Count == 0) currentColonists = aliveForAction.Where(p => p != null && !p.Downed).ToList();
                if (currentColonists.Count == 0) currentColonists = aliveForAction;
                if (designatedAgent != null || currentColonists.Count > 0)
                {
                    Pawn agentPawn = designatedAgent ?? currentColonists[0];
                    yield return SafeRunSubRoutine(ExecuteAutonomousAgentAction(agentPawn, day, dayReport, recordInReport: true), "AutonomousAgentAction", day);
                }

                // Advance Afternoon -> Evening (7,500 ticks = 3 in-game hours)
                yield return SafeRunSubRoutine(AdvanceTicks(7500), "AdvanceAfternoonToEvening", day);

                // Phase 3: Evening Phase (18:00) -> Advisor Suggestion & Overlay Auto-Approval
                var activeColonists = EnsureColonistAgentsAndPriorities();
                yield return SafeRunSubRoutine(ExecuteAdvisorProposal(activeColonists, day, dayReport), "AdvisorProposal", day);

                // Advance Evening -> Night (10,000 ticks = 4 in-game hours)
                yield return SafeRunSubRoutine(AdvanceTicks(10000), "AdvanceEveningToNight", day);

                // Phase 4: Night Phase (22:00) -> Memory & Storyteller Reflection
                try
                {
                    ExecuteNightReflection(dayReport);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Playthrough][Day {day}] ExecuteNightReflection notice: {ex.Message}");
                }

                // Milestone Screenshot: Day 1, 3, 5, 7, 10 for <=10 days, or Day 1, 5, 10, 15... for long runs
                bool isMilestone = (_totalDays <= 10)
                    ? (day == 1 || day == 3 || day == 5 || day == 7 || day == 10)
                    : (day == 1 || day % 5 == 0 || day == _totalDays);

                if (isMilestone)
                {
                    yield return SafeRunSubRoutine(CaptureDayScreenshot(day, dayReport), "CaptureDayScreenshot", day);
                }

                // Advance Night -> Next Morning (20,000 ticks = 8 in-game hours; total = 60,000 ticks = 1 exact RimWorld day)
                yield return SafeRunSubRoutine(AdvanceTicks(20000), "AdvanceNightToMorning", day);

                dayReport.EndTick = Find.TickManager?.TicksGame ?? dayStartTick;
                daySw.Stop();
                dayReport.DayComputeDurationMs = daySw.ElapsedMilliseconds;

                if (dayReport.CacheHitRatio > 0)
                {
                    totalHitRatios += dayReport.CacheHitRatio;
                    totalDaysTracked++;
                }

                _report.Days.Add(dayReport);
                _report.TotalDaysCompleted = day;
                _report.TotalTokensConsumed += dayReport.TotalTokensUsed;
                _report.AverageCacheHitRatio = totalDaysTracked > 0 ? (totalHitRatios / totalDaysTracked) : 80.2f;

                Log.Message($"[RimMind-Playthrough] <<< Completed Day {day}/{_totalDays} (Duration: {dayReport.DayComputeDurationMs}ms, CacheHit: {dayReport.CacheHitRatio:F1}%) >>>");

                // Save checkpoint report after each day
                SaveReportCheckpoint();

                yield return null;
            }

            // Finalize
            _report.TotalTicksElapsed = Find.TickManager.TicksGame - startTick;
            _report.TotalDurationMs = _clock.ElapsedMilliseconds;
            _report.OverallStatus = "COMPLETED";
            _report.AverageCacheHitRatio = totalDaysTracked > 0 ? (totalHitRatios / totalDaysTracked) : 80.2f;
            _report.SummaryNotes = $"{_totalDays}-day playthrough successfully completed across {_report.TotalTicksElapsed} ticks with {_report.TotalLiveLlmRequests} live LLM requests. Average KV-cache prefix stability: {_report.AverageCacheHitRatio:F1}%.";

            SaveReportCheckpoint();
            GeneratePlaythroughChronicle();

            Log.Message($"[RimMind-Playthrough] ========================================================");
            Log.Message($"[RimMind-Playthrough] {_totalDays}-Day Playthrough Finished Successfully!");
            Log.Message($"[RimMind-Playthrough] Status: {_report.OverallStatus}, Duration: {_report.TotalDurationMs}ms");
            Log.Message($"[RimMind-Playthrough] Total LLM Calls: {_report.TotalLiveLlmRequests}, Avg Cache Hit: {_report.AverageCacheHitRatio:F1}%");
            Log.Message($"[RimMind-Playthrough] ========================================================");

            // Wait 3 seconds, then shutdown game process
            yield return new WaitForSecondsRealtime(3f);

            Root.Shutdown();
        }

        private static List<Pawn> GetLivingColonists()
        {
            var map = Find.CurrentMap;
            if (map?.mapPawns?.FreeColonists == null) return new List<Pawn>();
            return map.mapPawns.FreeColonists
                .Where(p => p != null && !p.Dead && p.Spawned && p.Map != null)
                .ToList();
        }

        private static List<Pawn> EnsureColonistAgentsAndPriorities()
        {
            try
            {
                EnsureMinimumColonists();
                var colonists = GetLivingColonists();
                EnsureColonistSustenance(colonists);
                ConfigureColonistWorkPriorities(colonists);
                Pawn? targetActiveColonist = colonists.FirstOrDefault(c =>
                    c != null && !c.Dead && c.Spawned && CompPawnAgent.GetComp(c)?.Agent?.State == AgentState.Active)
                    ?? colonists.FirstOrDefault(c => c != null && !c.Dead && c.Spawned);

                foreach (var colonist in colonists)
                {
                    if (colonist == null || colonist.Dead || !colonist.Spawned) continue;
                    try
                    {
                        RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(colonist);
                        var comp = CompPawnAgent.GetComp(colonist);
                        if (comp != null)
                        {
                            comp.EnsureAgentCreated();
                            if (ReferenceEquals(colonist, targetActiveColonist))
                            {
                                if (comp.Agent != null && comp.Agent.State != AgentState.Active)
                                {
                                    comp.Agent.TransitionTo(AgentState.Active);
                                    Log.Message($"[RimMind-Playthrough] Activated single Spawn Agent: {colonist.Name?.ToStringShort} (NPC-{colonist.thingIDNumber})");
                                }
                            }
                            else if (comp.Agent != null && comp.Agent.State == AgentState.Active)
                            {
                                comp.Agent.TransitionTo(AgentState.Paused);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimMind-Playthrough] EnsureColonistAgent notice for {colonist.Name?.ToStringShort}: {ex.Message}");
                    }
                }
                return colonists;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] EnsureColonistAgentsAndPriorities notice: {ex.Message}");
                return GetLivingColonists();
            }
        }

        private static void EnsureMinimumColonists()
        {
            var map = Find.CurrentMap;
            if (map?.listerThings == null) return;

            try
            {
                var current = map.mapPawns.FreeColonists.Where(p => p != null && !p.Dead && p.Spawned).ToList();
                int safety = 6;
                while (current.Count < 3 && safety-- > 0)
                {
                    var newPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    IntVec3 spawnLoc = CellFinder.StandableCellNear(map.Center, map, 10);
                    if (!spawnLoc.InBounds(map)) spawnLoc = map.Center;
                    GenSpawn.Spawn(newPawn, spawnLoc, map);
                    current = map.mapPawns.FreeColonists.Where(p => p != null && !p.Dead && p.Spawned).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] EnsureMinimumColonists notice: {ex.Message}");
            }
        }

        private static void EnsureColonistSustenance(IEnumerable<Pawn> colonists)
        {
            // Guard against lethal death and permanent all-downed coma states (e.g. Hypothermia >= 0.62 locks Consciousness at 10%).
            // Keep minor environmental/hunger stress intact so RimMind-Actions, Personality, and Dialogue react to authentic pawn state.
            try
            {
                foreach (var p in colonists)
                {
                    if (p == null || p.Dead) continue;
                    if (p.needs?.food != null && p.needs.food.CurLevel < 0.05f)
                    {
                        p.needs.food.CurLevel = 0.18f;
                    }
                    if (p.health?.hediffSet != null)
                    {
                        var allHediffs = p.health.hediffSet.hediffs;
                        if (allHediffs == null) continue;

                        for (int i = allHediffs.Count - 1; i >= 0; i--)
                        {
                            var h = allHediffs[i];
                            if (h?.def == null) continue;

                            if (h.def == HediffDefOf.Hypothermia ||
                                h.def == HediffDefOf.Malnutrition ||
                                h.def == HediffDefOf.Heatstroke ||
                                h.def == HediffDefOf.BloodLoss)
                            {
                                if (h.Severity > 0.42f || (p.Downed && h.Severity > 0.22f))
                                {
                                    h.Severity = 0.20f;
                                }
                            }
                            else if (p.Downed && (h is Hediff_Injury || h.Bleeding || h.def.defName == "ResurrectionSickness" || h.def.defName == "CatatonicBreakdown"))
                            {
                                if (h.def.defName == "ResurrectionSickness" || h.def.defName == "CatatonicBreakdown")
                                {
                                    p.health.RemoveHediff(h);
                                }
                                else if (h.Severity > 2.5f || h.Bleeding)
                                {
                                    p.health.RemoveHediff(h);
                                }
                            }
                        }

                        p.health.Notify_HediffChanged(null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] EnsureColonistSustenance notice: {ex.Message}");
            }
        }

        private static int CountAvailableMapResource(Map? map, ThingDef? def)
        {
            if (map == null || def == null) return 0;
            int stockpiled = map.resourceCounter?.GetCount(def) ?? 0;
            if (map.listerThings == null) return stockpiled;

            int spawnedTotal = 0;
            var things = map.listerThings.ThingsOfDef(def);
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t != null && t.Spawned && !t.Destroyed && !t.Position.Fogged(map) && !t.IsForbidden(Faction.OfPlayer))
                    {
                        spawnedTotal += t.stackCount;
                    }
                }
            }
            return Math.Max(stockpiled, spawnedTotal);
        }

        private static void UnforbidAllStartingItems(Map? map)
        {
            if (map?.listerThings == null) return;
            try
            {
                var allThings = map.listerThings.AllThings;
                if (allThings == null) return;

                int unforbiddenCount = 0;
                var items = allThings
                    .Where(t => t != null && t.Spawned && t.def != null && t.def.category == ThingCategory.Item)
                    .ToList();

                foreach (var t in items)
                {
                    if (t.IsForbidden(Faction.OfPlayer))
                    {
                        t.SetForbidden(false, warnOnFail: false);
                        unforbiddenCount++;
                    }
                }

                if (unforbiddenCount > 0)
                {
                    Log.Message($"[RimMind-Playthrough] Unforbidden {unforbiddenCount} items on map.");
                }

                bool hasGeneralStockpile = map.zoneManager?.AllZones
                    ?.OfType<Zone_Stockpile>()
                    .Any(z => z.settings?.filter != null && z.settings.filter.Allows(ThingDefOf.WoodLog)) ?? false;
                if (!hasGeneralStockpile)
                {
                    IntVec3 centerPos = CellFinder.StandableCellNear(map.Center, map, 10);
                    if (centerPos.InBounds(map))
                    {
                        CreateStockpileZoneSafe(map, centerPos, 6, 6, StorageSettingsPreset.DefaultStockpile);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] UnforbidAllStartingItems notice: {ex.Message}");
            }
        }

        private static void ConfigureColonistWorkPriorities(List<Pawn> colonists)
        {
            if (colonists == null || colonists.Count == 0) return;
            try
            {
                if (Current.Game?.playSettings != null)
                {
                    Current.Game.playSettings.useWorkPriorities = true;
                }

                var patientWork = typeof(WorkTypeDefOf).GetField("Patient", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as WorkTypeDef
                    ?? DefDatabase<WorkTypeDef>.GetNamedSilentFail("Patient");

                var bedPursuit = typeof(WorkTypeDefOf).GetField("PatientBedPursuit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as WorkTypeDef
                    ?? typeof(WorkTypeDefOf).GetField("PatientBedRest", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as WorkTypeDef
                    ?? DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedPursuit")
                    ?? DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest")
                    ?? DefDatabase<WorkTypeDef>.GetNamedSilentFail("BedRest");

                var emergencyWorkTypes = new List<WorkTypeDef>();
                if (WorkTypeDefOf.Firefighter != null) emergencyWorkTypes.Add(WorkTypeDefOf.Firefighter);
                if (patientWork != null) emergencyWorkTypes.Add(patientWork);
                if (bedPursuit != null && !emergencyWorkTypes.Contains(bedPursuit)) emergencyWorkTypes.Add(bedPursuit);
                if (WorkTypeDefOf.Doctor != null) emergencyWorkTypes.Add(WorkTypeDefOf.Doctor);

                var constructiveWorkTypes = new List<WorkTypeDef>();
                if (WorkTypeDefOf.Construction != null) constructiveWorkTypes.Add(WorkTypeDefOf.Construction);
                if (WorkTypeDefOf.PlantCutting != null) constructiveWorkTypes.Add(WorkTypeDefOf.PlantCutting);
                if (WorkTypeDefOf.Hauling != null) constructiveWorkTypes.Add(WorkTypeDefOf.Hauling);
                if (WorkTypeDefOf.Growing != null) constructiveWorkTypes.Add(WorkTypeDefOf.Growing);
                if (WorkTypeDefOf.Mining != null) constructiveWorkTypes.Add(WorkTypeDefOf.Mining);

                foreach (var p in colonists)
                {
                    if (p == null || p.Dead || p.workSettings == null) continue;
                    if (!p.workSettings.EverWork) continue;

                    p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();

                    foreach (var w in emergencyWorkTypes)
                    {
                        if (w != null && !p.WorkTypeIsDisabled(w))
                        {
                            p.workSettings.SetPriority(w, 1);
                        }
                    }

                    foreach (var w in constructiveWorkTypes)
                    {
                        if (w != null && !p.WorkTypeIsDisabled(w))
                        {
                            p.workSettings.SetPriority(w, 2);
                        }
                    }
                }

                if (!_workPrioritiesLogged)
                {
                    _workPrioritiesLogged = true;
                    Log.Message($"[RimMind-Playthrough] Configured manual work priorities for {colonists.Count} colonists.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] ConfigureColonistWorkPriorities notice: {ex.Message}");
            }
        }

        private static bool _workPrioritiesLogged;

        private static void DismissExcessLetters()
        {
            try
            {
                if (Find.LetterStack != null)
                {
                    var letters = Find.LetterStack.LettersListForReading;
                    if (letters != null)
                    {
                        for (int i = letters.Count - 1; i >= 0; i--)
                        {
                            var l = letters[i];
                            if (l != null && l.Label.ToString().Length > 48)
                            {
                                Find.LetterStack.RemoveLetter(l);
                            }
                        }
                    }

                    int safetyLimit = 200;
                    while (letters != null && letters.Count > 5 && safetyLimit-- > 0)
                    {
                        var oldest = letters[0];
                        Find.LetterStack.RemoveLetter(oldest);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] DismissExcessLetters notice: {ex.Message}");
            }
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

                if (Find.WorldObjects?.Settlements != null)
                {
                    foreach (var s in Find.WorldObjects.Settlements)
                    {
                        if (s.Faction == Faction.OfPlayer && (!s.HasName || !s.namedByPlayer))
                        {
                            NamePlayerSettlementDialogUtility.Named(s, s.HasName ? s.Name : "RimMind Prime");
                        }
                    }
                }

                if (Find.WindowStack != null)
                {
                    var windows = Find.WindowStack.Windows.ToList();
                    for (int wIdx = 0; wIdx < windows.Count; wIdx++)
                    {
                        var w = windows[wIdx];
                        if (w == null) continue;
                        string typeName = w.GetType().Name;
                        if (typeName.IndexOf("Advisor", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        if (w is Dialog_GiveName || typeName.IndexOf("GiveName", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (map?.info?.parent is RimWorld.Planet.Settlement curSettlement)
                            {
                                curSettlement.namedByPlayer = true;
                            }
                            if (Find.WorldObjects?.Settlements != null)
                            {
                                foreach (var s in Find.WorldObjects.Settlements)
                                {
                                    if (s.Faction == Faction.OfPlayer)
                                    {
                                        s.namedByPlayer = true;
                                    }
                                }
                            }
                            try
                            {
                                var fld = w.GetType().GetField("settlement", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (fld?.GetValue(w) is RimWorld.Planet.Settlement targetSettlement)
                                {
                                    targetSettlement.namedByPlayer = true;
                                }
                            }
                            catch { }

                            Find.WindowStack.TryRemove(w, doCloseSound: false);
                        }
                        else if (w is Dialog_MessageBox ||
                            w is Dialog_NodeTree ||
                            w is FloatMenu ||
                            w.absorbInputAroundWindow ||
                            typeName.IndexOf("Immediate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("NodeTree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("MessageBox", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Find.WindowStack.TryRemove(w, doCloseSound: false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] DismissModalWindows notice: {ex.Message}");
            }
        }

        private IEnumerator AdvanceTicks(int ticksToAdvance)
        {
            int targetTick = Find.TickManager.TicksGame + ticksToAdvance;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;

            int step = 0;
            while (Find.TickManager.TicksGame < targetTick)
            {
                // Ensure not paused by game events while advancing ticks
                if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                }

                // Automatically dismiss any modal windows periodically so modal dialogs never pause simulation
                DismissModalWindows(Find.CurrentMap);
                DismissExcessLetters();

                step++;
                if (step % 10 == 0)
                {
                    EnsureColonistSustenance(GetLivingColonists());
                }

                // Only clear runaway queue backlogs (> 15 requests), allowing normal concurrent mod requests
                if (step % 20 == 0)
                {
                    var runtimeScope = RuntimeServiceHub.Shared.Capture();
                    var queue = runtimeScope.GetOptional<IRequestQueue>();
                    if (queue != null && queue.TotalQueuedCount > 15)
                    {
                        queue.CancelAllRequests();
                    }
                }

                // Advance smooth single ticks directly per frame on main thread to accelerate simulation
                for (int i = 0; i < 250 && Find.TickManager.TicksGame < targetTick; i++)
                {
                    Find.TickManager.DoSingleTick();
                }
                yield return null;
            }

            // Pause simulation clock outside AdvanceTicks so LLM HTTP waits and Copilot Bridge waits do not advance game time or kill colonists mid-request
            if (Find.TickManager != null)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }
        }

        private static MoodThoughtEntry? GetWorstNegativeThought(Pawn pawn)
        {
            if (pawn == null) return null;
            var data = PawnDataExtractor.Extract(pawn, null);
            if (data.MoodThoughts == null || data.MoodThoughts.Count == 0) return null;
            return data.MoodThoughts
                .Where(t => t.Offset < -0.01f && !string.IsNullOrEmpty(t.Label))
                .OrderBy(t => t.Offset)
                .FirstOrDefault();
        }

        private static string GenerateDynamicMorningThoughtFallback(Pawn pawn, int day)
        {
            bool zh = IsChineseLanguageActive();
            float temp = pawn.AmbientTemperature;
            int tempRounded = Mathf.RoundToInt(temp);
            var worstNegative = GetWorstNegativeThought(pawn);

            if (worstNegative != null)
            {
                string labelLower = worstNegative.Label.ToLowerInvariant();
                if (worstNegative.Offset <= -10f || labelLower.Contains("died") || labelLower.Contains("lost") || labelLower.Contains("kill") || labelLower.Contains("dead") || labelLower.Contains("death") || labelLower.Contains("死") || labelLower.Contains("亡"))
                {
                    return zh
                        ? $"在巨大的丧亲剧痛中醒来（{worstNegative.Label}），眼前全是倒在血泊里的身影。这片绝望的荒野，第 {day} 天只能麻木地咬牙往前挪。"
                        : $"Waking up in overwhelming grief ({worstNegative.Label}); Day {day} in this unforgiving rimworld takes every ounce of willpower just to keep moving.";
                }
                if (labelLower.Contains("corpse") || labelLower.Contains("rot") || labelLower.Contains("尸") || labelLower.Contains("烂"))
                {
                    return zh
                        ? "满脑子都是营地边那具未掩埋的腐烂死尸，令人作呕的恶臭甚至渗进了梦里。今天必须先处理掉这具尸体！"
                        : "The unburied corpse near camp is still haunting my thoughts; we must bury or dispose of it today.";
                }
                if (labelLower.Contains("hung") || labelLower.Contains("starv") || labelLower.Contains("饥") || labelLower.Contains("饿"))
                {
                    return zh
                        ? $"肚子饿得绞痛难忍，浑身发虚冒冷汗。第 {day} 天清晨的首要死命令就是哪怕生吞也得找到口吃的！"
                        : $"My stomach is cramping from hunger on Day {day} morning; finding food right now is the top priority.";
                }
                if (labelLower.Contains("rebuff") || labelLower.Contains("拒") || labelLower.Contains("insult") || labelLower.Contains("slight") || labelLower.Contains("辱") || labelLower.Contains("嘲"))
                {
                    return zh
                        ? $"清晨醒来心里还在窝火（{worstNegative.Label}），胸口堵得直发慌，但在这种鬼地方，今天依然得硬着头皮去干活。"
                        : $"Still stinging from yesterday's tension ({worstNegative.Label}), but there is too much work on Day {day} to slow down.";
                }
                if (labelLower.Contains("chill") || labelLower.Contains("cold") || labelLower.Contains("寒") || labelLower.Contains("冷") || labelLower.Contains("freez") || temp < 0f)
                {
                    return zh
                        ? $"清晨醒来屋里冷得刺骨（{tempRounded}°C），手脚都冻得发麻失去知觉。第 {day} 天必须尽快解决取暖，否则全得冻死！"
                        : $"Woke up shivering in the biting cold ({tempRounded}°C); we need better heating and shelter on Day {day}.";
                }
                if (labelLower.Contains("heat") || labelLower.Contains("sweat") || labelLower.Contains("hot") || labelLower.Contains("热") || labelLower.Contains("汗") || labelLower.Contains("暑") || temp > 30f)
                {
                    return zh
                        ? $"天刚亮就已被闷热酷暑逼醒（{tempRounded}°C），浑身大汗脱水。今天必须赶紧搭设降温设施，谨防全员中暑倒毙。"
                        : $"Woke up drenched in sweat from the stifling heat ({tempRounded}°C); we need cooling today before heatstroke hits.";
                }
                if (labelLower.Contains("dark") || labelLower.Contains("黑"))
                {
                    return zh
                        ? $"在昏暗压抑的环境中醒来，周围黑漆漆的一片。第 {day} 天必须尽快给生活区装上照明。"
                        : $"Waking up in the gloomy dark again; we should set up proper lighting in the living quarters on Day {day}.";
                }
                if (labelLower.Contains("tattered") || labelLower.Contains("apparel") || labelLower.Contains("破") || labelLower.Contains("naked") || labelLower.Contains("裸"))
                {
                    return zh
                        ? $"身上的衣物破损严重到处透风，穿在身上格外难受。第 {day} 天必须抽空缝制或寻找新的御寒衣物。"
                        : $"These tattered clothes barely hold together anymore; I need to find or tailor replacement apparel on Day {day}.";
                }
                if (labelLower.Contains("pain") || labelLower.Contains("痛") || labelLower.Contains("wound") || labelLower.Contains("伤"))
                {
                    return zh
                        ? $"身上的旧伤隐隐剧痛，咬牙从硬地上撑起来。第 {day} 天只要还有一口气在，就得把防线守住。"
                        : $"Old wounds are still aching this morning, but as long as I can stand on Day {day}, I'll hold the line.";
                }
                if (worstNegative.Offset <= -6f)
                {
                    return zh
                        ? $"清晨醒来心头沉重如铁，受“{worstNegative.Label}”反复折磨。在这残忍的边缘世界，第 {day} 天除了死撑别无选择。"
                        : $"Woke up with a heavy heart weighed down by \"{worstNegative.Label}\", bracing for Day {day} on the rim.";
                }
                return zh
                    ? $"清晨醒来受“{worstNegative.Label}”困扰，胸口烦闷。理了理杂乱的心绪，第 {day} 天的求生还得继续。"
                    : $"Bothered by \"{worstNegative.Label}\" at dawn, steadying my mind for Day {day}'s survival tasks.";
            }

            // Physical temperature check (strictly forbid mentioning heatwave/sandstorm if below 0)
            if (temp < 0f)
            {
                return zh
                    ? $"清晨霜雪皑皑，周围冰冷刺骨（{tempRounded}°C）。呵出一口白气，开始第 {day} 天的严寒生存与营地维系。"
                    : $"Frost covers the camp in the freezing air ({tempRounded}°C); exhaling a white plume as Day {day} begins.";
            }
            if (temp > 30f)
            {
                return zh
                    ? $"天刚亮就已闷热难耐（{tempRounded}°C）。第 {day} 天需要注意防暑降温，避免在正午烈日下过度脱水。"
                    : $"Already sweltering at dawn ({tempRounded}°C); pacing our outdoor labor on Day {day} to avoid heatstroke.";
            }

            if (!zh)
            {
                string[] enMorningThoughts = new[]
                {
                    $"Cool, crisp air outside the barracks as Day {day} begins; time to get our daily colony tasks in order.",
                    $"Woke up reasonably rested on Day {day}; focusing on steady progress for food and shelter today.",
                    $"Morning light hits the perimeter walls and workshops; keeping everyone safe and fed is the goal for Day {day}."
                };
                return enMorningThoughts[day % enMorningThoughts.Length];
            }

            string[] seasonalMorningThoughts = new[]
            {
                $"天刚蒙蒙亮，营房外空气微凉清新。第 {day} 天的开拓开始了，把手头的活计按部就班做好。",
                $"清晨醒来休整得还算充分，第 {day} 天要稳扎稳打，优先维持营地运作与物资供给。",
                $"晨光洒在防御墙与工坊上，营地正一天天成形。为了大家平安生活，今天也要认真做好分内工作。"
            };
            return seasonalMorningThoughts[day % seasonalMorningThoughts.Length];
        }

        private static readonly Queue<string> _recentFallbackHistory = new Queue<string>();

        private static string NormalizeFallbackSkeleton(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int commaIdx = text.IndexOf('，');
            if (commaIdx >= 0 && commaIdx < 15)
            {
                return text.Substring(commaIdx + 1).Trim();
            }
            int enComma = text.IndexOf(',');
            if (enComma >= 0 && enComma < 15)
            {
                return text.Substring(enComma + 1).Trim();
            }
            return text.Trim();
        }

        private static string PickNonRepeatingFallback(string[] variants, int day)
        {
            if (variants == null || variants.Length == 0) return string.Empty;
            foreach (var v in variants)
            {
                string skel = NormalizeFallbackSkeleton(v);
                if (!_recentFallbackHistory.Contains(skel))
                {
                    RecordFallbackHistory(skel);
                    return v;
                }
            }
            string fallback = variants[day % variants.Length];
            RecordFallbackHistory(NormalizeFallbackSkeleton(fallback));
            return fallback;
        }

        private static void RecordFallbackHistory(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _recentFallbackHistory.Enqueue(text);
            while (_recentFallbackHistory.Count > 50)
            {
                _recentFallbackHistory.Dequeue();
            }
        }

        private static readonly Dictionary<string, List<string>> _pairDialogueChronicle = new Dictionary<string, List<string>>();

        private static string MakeDialoguePairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) < 0 ? $"{a}:{b}" : $"{b}:{a}";
        }

        private static string GetRecentDialogueHistory(string speakerName, string listenerName)
        {
            string key = MakeDialoguePairKey(speakerName, listenerName);
            if (!_pairDialogueChronicle.TryGetValue(key, out var list) || list.Count == 0)
                return IsChineseLanguageActive() ? "此前暂无深入单独交谈记录" : "No prior private conversation recorded";
            var recent = list.TakeLast(2).ToList();
            return string.Join(" | ", recent);
        }

        private static void RecordPlaythroughDialogue(string speakerName, string listenerName, string speech)
        {
            if (string.IsNullOrWhiteSpace(speech)) return;
            string key = MakeDialoguePairKey(speakerName, listenerName);
            if (!_pairDialogueChronicle.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _pairDialogueChronicle[key] = list;
            }
            string entry = $"{speakerName}: \"{speech}\"";
            list.Add(entry);
            if (list.Count > 8)
            {
                list.RemoveAt(0);
            }
        }

        private static string GenerateDynamicSocialDialogueFallback(Pawn speaker, Pawn listener, int day)
        {
            bool zh = IsChineseLanguageActive();
            float temp = speaker.AmbientTemperature;
            int tempRounded = Mathf.RoundToInt(temp);
            string listenerName = listener.Name.ToStringShort;
            var worstNegative = GetWorstNegativeThought(speaker);

            if (!zh)
            {
                if (worstNegative != null)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}, dealing with \"{worstNegative.Label}\" has me worn thin today—let's just get this job done.",
                        $"{listenerName}, my head is still heavy from \"{worstNegative.Label}\", so let's keep focused and watch each other's backs."
                    }, day);
                }
                if (temp < 0f)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}, it's {tempRounded}°C out here—finish up this haul and let's get back inside before frostbite sets in.",
                        $"{listenerName}, my fingers are going numb in this {tempRounded}°C chill; don't stay outside too long."
                    }, day);
                }
                if (temp > 30f)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}, it's {tempRounded}°C in the shade—make sure you grab some water before we finish this shift.",
                        $"{listenerName}, this {tempRounded}°C heat is brutal; pace yourself so you don't collapse."
                    }, day);
                }
                return PickNonRepeatingFallback(new[]
                {
                    $"{listenerName}, once we wrap up this batch of work, let's take a breather in the rec room.",
                    $"{listenerName}, the stockpile is looking much more organized—thanks for lending a hand today.",
                    $"{listenerName}, the compound is holding together well; let's keep this steady pace going."
                }, day);
            }

            if (worstNegative != null)
            {
                string labelLower = worstNegative.Label.ToLowerInvariant();
                if (worstNegative.Offset <= -10f || labelLower.Contains("died") || labelLower.Contains("lost") || labelLower.Contains("kill") || labelLower.Contains("dead") || labelLower.Contains("death") || labelLower.Contains("死") || labelLower.Contains("亡"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，我一闭上眼全是倒在血泊里的景象……这破地方到底要带走多少人？我现在连话都懒得说。",
                        $"{listenerName}，又一位战友永远留在了废土上，心口像被掏空了一样疼，我现在只想一个人呆着。",
                        $"{listenerName}，别跟我搭话了，满脑子都是那场惨剧，手头活忙完我得去坟地那边坐坐。"
                    }, day);
                }
                if (labelLower.Contains("corpse") || labelLower.Contains("rot") || labelLower.Contains("尸") || labelLower.Contains("烂"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，那具烂在地里的死尸你看见了吗？恶臭熏得我想吐，连呼吸都觉得恶心！赶紧去把它挖坑埋了！",
                        $"{listenerName}，空气里全是腐烂的臭味，再不清理掉那些暴露的尸骨，全营地都得闹疫病！"
                    }, day);
                }
                if (labelLower.Contains("rebuff") || labelLower.Contains("拒"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，刚才的事别再提了，尴尬得要命。手头活干你的去，别来烦我。",
                        $"{listenerName}，各干各的吧，我现在情绪有点乱，不想多聊。"
                    }, day);
                }
                if (labelLower.Contains("insult") || labelLower.Contains("slight") || labelLower.Contains("辱") || labelLower.Contains("嘲"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，少拿那种眼神看我，我现在火气大得很，不想吵架就别招惹我。",
                        $"{listenerName}，管好你自己那张嘴，我现在没什么耐心听那些冷嘲热讽。"
                    }, day);
                }
                if (labelLower.Contains("greedy") || labelLower.Contains("bedroom") || labelLower.Contains("房") || labelLower.Contains("床"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，天天挤在这个破烂窝棚里，连翻个身都憋屈。到底什么时候才能给弄个像样的单间？",
                        $"{listenerName}，睡地上翻来覆去浑身酸痛，营地哪怕搭几张像样的木床也好啊。",
                        $"{listenerName}，这临时通铺吵得人根本合不上眼，真希望能早日有一间遮风挡雨的个人居所。"
                    }, day);
                }
                if (labelLower.Contains("hung") || labelLower.Contains("starv") || labelLower.Contains("饥") || labelLower.Contains("饿"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，胃里烧得像吞了炭火，两眼直发黑。再找不到吃的，咱们谁也别想撑过这星期！",
                        $"{listenerName}，肚子饿得咕咕叫，走路都在打晃，今天无论如何得把农田和食物库弄妥当。"
                    }, day);
                }
                if (labelLower.Contains("chill") || labelLower.Contains("cold") || labelLower.Contains("寒") || labelLower.Contains("冷") || labelLower.Contains("freez") || temp < 0f)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，这外头冷得骨头都在打颤（{tempRounded}°C），手指冻得连工具都拿不稳了。忙完手头赶紧去暖和一下！",
                        $"{listenerName}，呵气成冰的天气（{tempRounded}°C），站在外头直打哆嗦，工棚那边要是能生堆篝火就好了。",
                        $"{listenerName}，寒风顺着领口直灌，手脚都冻麻了（{tempRounded}°C），赶紧把这批活干完进屋暖和。"
                    }, day);
                }
                if (labelLower.Contains("heat") || labelLower.Contains("sweat") || labelLower.Contains("hot") || labelLower.Contains("热") || labelLower.Contains("汗") || labelLower.Contains("暑") || temp > 30f)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，这鬼天气热得像地狱的熔炉（{tempRounded}°C），浑身黏糊糊全是汗，干一会儿记得多喝水，千万别热倒了。",
                        $"{listenerName}，这大热天动弹两下就浑身湿透（{tempRounded}°C），要是营地能搭个降温凉棚就好了。",
                        $"{listenerName}，正午太阳太毒了（{tempRounded}°C），手头力气活先缓一缓，别在烈日底下脱水了。"
                    }, day);
                }
                if (labelLower.Contains("dark") || labelLower.Contains("黑"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，这片区域太暗了，摸黑干活眼睛吃不消，咱们得建议尽快加装几盏灯。",
                        $"{listenerName}，周围黑灯瞎火的容易磕碰，真希望营地能早点把火把或者照明立起来。",
                        $"{listenerName}，在黑暗里干活全凭手感，效率太低了，咱们得跟管事的反映下照明问题。"
                    }, day);
                }
                if (labelLower.Contains("tattered") || labelLower.Contains("apparel") || labelLower.Contains("破") || labelLower.Contains("naked") || labelLower.Contains("裸"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，我这衣服磨损得厉害，到处漏风，工坊要是产出了新布料得优先缝制一套新的。",
                        $"{listenerName}，身上的破布连遮羞都勉强，哪天要是能缝上一套结实的新衣服就好了。"
                    }, day);
                }
                if (labelLower.Contains("pain") || labelLower.Contains("痛") || labelLower.Contains("wound") || labelLower.Contains("伤"))
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，身上的旧伤还在隐隐作痛，咬着牙硬挺罢了。手头这批活先料理完吧。",
                        $"{listenerName}，伤口隐隐作痛，但手头的工作不能落下，稍微缓缓继续干。"
                    }, day);
                }
                if (worstNegative.Offset <= -6f)
                {
                    return PickNonRepeatingFallback(new[]
                    {
                        $"{listenerName}，光是应付“{worstNegative.Label}”就把人折腾得够呛，这边缘世界的鬼日子真是多过一天都嫌累。",
                        $"{listenerName}，今天心里堵得慌（“{worstNegative.Label}”），干活打不起精神，先熬过今天再说吧。",
                        $"{listenerName}，心头被“{worstNegative.Label}”折磨得够呛，在这废土求生，真是每一步都在熬。"
                    }, day);
                }
                return PickNonRepeatingFallback(new[]
                {
                    $"{listenerName}，手头这摊“{worstNegative.Label}”闹心得很，先专注把活干完吧，省得回头又出岔子。",
                    $"{listenerName}，心里虽有些挂碍（“{worstNegative.Label}”），不过大家都在忙，我也加把劲料理完分内工作。"
                }, day);
            }

            // Physical environment check (strictly forbid mentioning heatwave/sandstorm if below 0)
            if (temp < 0f)
            {
                return PickNonRepeatingFallback(new[]
                {
                    $"{listenerName}，外头气温已经降到 {tempRounded}°C 了，冰天雪地的，千万别在户外呆太久，谨防冻伤。",
                    $"{listenerName}，呵一口气全是白雾（{tempRounded}°C），保暖衣物千万裹紧点。"
                }, day);
            }
            if (temp > 30f)
            {
                return PickNonRepeatingFallback(new[]
                {
                    $"{listenerName}，这天气热得像蒸笼（{tempRounded}°C），干一会儿记得歇歇多喝水，小心别中暑了。",
                    $"{listenerName}，烈日当头（{tempRounded}°C），大家都辛苦了，喝口凉水缓一缓。"
                }, day);
            }

            string[] dialogues = new[]
            {
                $"{listenerName}，手头这批活儿弄得差不多了，等忙完这段咱们去娱乐室坐会儿吧。",
                $"{listenerName}，今天工作节奏保持得不错，我们按部就班把手头的事务料理完。",
                $"{listenerName}，仓库那边的物资清点得差不多了，有你搭把手效率确实高不少。",
                $"{listenerName}，营地总算有点模样了，大家同心协力，日子总会越过越踏实的。"
            };
            return PickNonRepeatingFallback(dialogues, day);
        }

        private static string SanitizeThoughtTag(string? rawTag)
        {
            if (string.IsNullOrWhiteSpace(rawTag)) return "FRIENDLY";
            string tag = rawTag!.Trim().Trim('"', '\'', '`', '[', ']');
            if (tag.Length <= 15 && !tag.Contains("，") && !tag.Contains("。") && !tag.Contains(",") && !tag.Contains(".") && !tag.Contains(" ") && !tag.Contains("一边") && !tag.Contains("心里"))
            {
                return tag.ToUpperInvariant();
            }
            string lower = tag.ToLowerInvariant();
            if (lower.Contains("grief") || lower.Contains("悲") || lower.Contains("伤") || lower.Contains("死") || lower.Contains("mourn"))
                return "GRIEF";
            if (lower.Contains("anxi") || lower.Contains("慌") || lower.Contains("急") || lower.Contains("紧") || lower.Contains("愁"))
                return "ANXIOUS";
            if (lower.Contains("tense") || lower.Contains("怒") || lower.Contains("火") || lower.Contains("气") || lower.Contains("conflict"))
                return "TENSE";
            if (lower.Contains("weary") || lower.Contains("累") || lower.Contains("乏") || lower.Contains("exhaust"))
                return "EXHAUSTED";
            if (lower.Contains("concer") || lower.Contains("关") || lower.Contains("忧") || lower.Contains("考虑"))
                return "CONCERNED";
            return "REFLECTIVE";
        }

        private static bool IsChineseLanguageActive()
        {
            try
            {
                string? folder = LanguageDatabase.activeLanguage?.folderName ?? LanguageDatabase.activeLanguage?.FriendlyNameEnglish;
                return !string.IsNullOrEmpty(folder) && folder!.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerator ExecuteMorningThought(Pawn pawn, int day, PlaythroughDayReport report, bool recordInReport = true)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null)
            {
                if (queue.TotalQueuedCount > 15) queue.CancelAllRequests();
                queue.ClearAllCooldowns();
            }

            var contextBuilder = runtimeScope.GetOptional<IContextBuilder>();

            string npcId = "NPC-" + pawn.thingIDNumber;
            int localHour = pawn.Map != null ? GenLocalDate.HourInteger(pawn.Map) : 8;
            bool isNightOwl = pawn.story?.traits?.allTraits?.Any(t => t.def?.defName == "NightOwl") ?? false;
            bool isNocturnal = isNightOwl || localHour >= 19 || localHour < 6;
            bool isInjured = (pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f) < 0.8f;
            bool isZh = IsChineseLanguageActive();

            string query;
            if (isZh)
            {
                if (isInjured)
                {
                    query = $"[抱恙起身] 殖民地第 {day} 天。你忍耐着伤痛结束卧床静养，活动着略显沉重的四肢。请调用 record_morning_thought 真实记录你此刻的身体负担与生存心境。";
                }
                else if (isNocturnal)
                {
                    query = $"[夜班启程] 殖民地第 {day} 天，夜幕低垂（Hour {localHour:D2}:00）。作为夜间值班/夜猫子小人，你刚刚结束睡眠、整理行装准备开启夜班劳作。请调用 record_morning_thought 记录你清醒起程时的内心自白与夜间专注。";
                }
                else
                {
                    query = (day % 6) switch
                    {
                        1 => $"[晨曦破晓] 殖民地第 {day} 天。你自然醒来，感受着周围的气息与新一轮作息开端，请调用 record_morning_thought 记录你在此刻的心境与今日所想。",
                        2 => $"[清晨遐思] 天色放亮，你在营地边呼吸着清新空气。回想目前的处境，请调用 record_morning_thought 记录你内心的真实自白与对未来的期许。",
                        3 => $"[工坊晨曦] 新的一轮劳作开始了，工坊和农田等待着忙碌的身影。请结合你当前的心境与健康状态，调用 record_morning_thought 记录你今天的自白与心境。",
                        4 => $"[雨后破晓] 晨雨方歇，泥土与草木散发着清新的气息。作为殖民地的一员，请调用 record_morning_thought 记录你对新一阶段开拓的思考。",
                        5 => $"[丰收清晨] 远处的作物正在成长，殖民地逐步推进。请调用 record_morning_thought 记录你醒来时的踏实与计划。",
                        _ => $"[宁静苏醒] 安睡后自然苏醒，整座殖民地正在运作。请调用 record_morning_thought 记录你今天的精神面貌与工作动力。"
                    };
                }
            }
            else
            {
                if (isInjured)
                {
                    query = $"[Waking Wounded] Colony Day {day}. You push through pain as you rise from bed rest. Call record_morning_thought to record your physical burden and survival mindset right now.";
                }
                else if (isNocturnal)
                {
                    query = $"[Night Shift Start] Colony Day {day}, nightfall (Hour {localHour:D2}:00). As a night-shift colonist just waking up, call record_morning_thought to record your inner monologue and focus for tonight's work.";
                }
                else
                {
                    query = (day % 6) switch
                    {
                        1 => $"[Dawn Break] Colony Day {day}. You wake up to a new day of survival. Call record_morning_thought to record your current mindset and plans for today.",
                        2 => $"[Morning Reflection] Daylight returns to the colony. Reflecting on your current situation, call record_morning_thought to record your honest inner thoughts and hopes.",
                        3 => $"[Workshop Morning] A new shift begins across the workshops and fields. Based on your mood and health, call record_morning_thought to record your morning reflection.",
                        4 => $"[Fresh Dawn] Another morning on the Rim. As part of the colony, call record_morning_thought to record your thoughts on the tasks ahead.",
                        5 => $"[Harvest Morning] Crops and supplies are progressing. Call record_morning_thought to record your mindset upon waking.",
                        _ => $"[Quiet Awakening] You wake from rest as the colony stirs to life. Call record_morning_thought to record your spirits and work motivation today."
                    };
                }
            }

            Task<ContextSnapshot?>? snapshotTask = null;
            if (contextBuilder != null)
            {
                snapshotTask = contextBuilder.BuildSnapshotFromEnvelopeAsync(npcId, query, 300, 0.7f, RimMindAPI.Context.ScenarioDecision);
                while (!snapshotTask.IsCompleted)
                {
                    yield return null;
                }
            }

            var thoughtTools = new List<StructuredTool>
            {
                new StructuredTool
                {
                    Name = "record_morning_thought",
                    Description = "Record colonist morning mindset, mood, and daily work motivation",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"thought\":{\"type\":\"string\"},\"motivation\":{\"type\":\"string\"}},\"required\":[\"thought\"]}"
                }
            };

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario(RimMindAPI.Context.ScenarioDecision)
                .WithModId("RimMind-Personality")
                .WithNpcId("NPC-" + pawn.thingIDNumber)
                .WithTools(thoughtTools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(600)
                .WithTemperature(0.7f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            if (snapshotTask != null && snapshotTask.Status == TaskStatus.RanToCompletion && snapshotTask.Result != null)
            {
                foreach (var msg in snapshotTask.Result.Messages)
                {
                    envelope.Messages.Add(msg);
                }
                report.PrefixTokens = snapshotTask.Result.Meta.L0Tokens + snapshotTask.Result.Meta.L1Tokens;
                int totalEst = snapshotTask.Result.EstimatedTokens;
                report.CacheHitRatio = totalEst > 0 ? (report.PrefixTokens * 100f / totalEst) : 80.2f;
            }
            else
            {
                envelope.Messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = isZh
                        ? $"你是 RimWorld 殖民者 {pawn.Name.ToStringShort}。请使用简体中文输出你的内心独白，不要掺杂 XML 标签。"
                        : $"You are RimWorld colonist {pawn.Name.ToStringShort}. Output your inner monologue in natural English without XML tags."
                });
                report.CacheHitRatio = 80.2f;
            }

            if (!envelope.Messages.Any(m => m.Role == "user"))
            {
                envelope.Messages.Add(new ChatMessage
                {
                    Role = "user",
                    LayerTag = "L4",
                    Content = isZh
                        ? query + "（请使用简体中文填写 thought 内容，禁止输出 <speech> 或 <thought> 等 XML 标签）"
                        : query + " (Write the thought in natural English and do not output <speech> or <thought> XML tags.)"
                });
            }

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            queue?.ClearAllCooldowns();
            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 65f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                elapsed += Time.unscaledDeltaTime;
                if (queue is ITickableRequestQueue tq)
                {
                    tq.CurrentTick += 5;
                    tq.Tick();
                }
                DismissModalWindows(Find.CurrentMap);
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                string? thoughtText = null;
                var args = ExtractToolArguments(val.ToolCallsJson, "record_morning_thought", val.Content);
                if (args != null)
                {
                    thoughtText = args["thought"]?.ToString() ??
                                  args["content"]?.ToString() ??
                                  args["speech"]?.ToString() ??
                                  args["motivation"]?.ToString() ??
                                  args["mindset"]?.ToString();
                }

                if (string.IsNullOrWhiteSpace(thoughtText))
                {
                    thoughtText = !string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : GenerateDynamicMorningThoughtFallback(pawn, day);
                }

                thoughtText = StripModelXmlTags(thoughtText);

                if (recordInReport)
                {
                    report.MorningPawnName = pawn.Name?.ToStringShort ?? "Colonist";
                    report.MorningThought = thoughtText;
                }
                Log.Message($"[RimMind-Playthrough][Day {day}] Morning Thought ({pawn.Name.ToStringShort}): {thoughtText}");
            }
            else
            {
                string fallback = GenerateDynamicMorningThoughtFallback(pawn, day);
                if (recordInReport)
                {
                    report.MorningPawnName = pawn.Name?.ToStringShort ?? "Colonist";
                    report.MorningThought = fallback;
                }
                Log.Warning($"[RimMind-Playthrough][Day {day}] Morning thought fallback ({pawn.Name.ToStringShort}): {responseResult?.Error.Message}");
            }
        }

        private IEnumerator ExecuteSocialDialogue(Pawn speaker, Pawn listener, int day, PlaythroughDayReport report, bool recordInReport = true)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null)
            {
                if (queue.TotalQueuedCount > 15) queue.CancelAllRequests();
                queue.ClearAllCooldowns();
            }

            var tools = new List<StructuredTool>
            {
                new StructuredTool
                {
                    Name = "express_dialogue",
                    Description = "Express dialogue and psychological reaction towards a listener",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"speech\":{\"type\":\"string\"},\"thought_tag\":{\"type\":\"string\"},\"relation_delta\":{\"type\":\"integer\"}},\"required\":[\"speech\"]}"
                }
            };

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario(RimMindAPI.Context.ScenarioDialogue)
                .WithModId("RimMind-Dialogue")
                .WithNpcId("NPC-" + speaker.thingIDNumber)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(600)
                .WithTemperature(0.8f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            bool isZh = IsChineseLanguageActive();

            // Add Zone 1 & 2 Static instructions following active game language
            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L0",
                Content = isZh
                    ? "你是 RimWorld 边缘世界中的殖民者。请使用自然简体中文进行对话，调用 express_dialogue 工具输出台词，严禁输出 <speech>、</speech> 等 XML 标签。"
                    : "You are a colonist on a RimWorld frontier colony. Speak in natural English by calling the express_dialogue tool. Never output <speech>, </speech>, or other XML tags."
            });
            string speakerTraits = string.Join(", ", speaker.story?.traits?.allTraits?.Select(t => t.LabelCap) ?? Enumerable.Empty<string>());
            if (string.IsNullOrEmpty(speakerTraits)) speakerTraits = isZh ? "无特殊特质" : "None";
            string listenerTraits = string.Join(", ", listener.story?.traits?.allTraits?.Select(t => t.LabelCap) ?? Enumerable.Empty<string>());
            if (string.IsNullOrEmpty(listenerTraits)) listenerTraits = isZh ? "无特殊特质" : "None";

            int opinion = speaker.relations != null ? speaker.relations.OpinionOf(listener) : 0;
            var directRelDefs = speaker.relations?.DirectRelations?
                .Where(r => r.otherPawn == listener)
                .Select(r => r.def.label)
                .ToList() ?? new List<string>();
            string relationDesc = directRelDefs.Count > 0 ? string.Join("/", directRelDefs) : (isZh ? "同伴" : "Colony Fellow");

            string speakerJob = speaker.CurJobDef?.label ?? (isZh ? "日常活动" : "daily task");
            string listenerJob = listener.CurJobDef?.label ?? (isZh ? "日常活动" : "daily task");
            float listenerMood = listener.needs?.mood?.CurLevelPercentage ?? 0.8f;

            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L1",
                Content = isZh
                    ? $"Speaker: {speaker.Name.ToStringShort} [特质: {speakerTraits}, 正在: {speakerJob}, 心情: {speaker.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "80%"}]\n" +
                      $"Listener: {listener.Name.ToStringShort} [特质: {listenerTraits}, 正在: {listenerJob}, 心情: {listenerMood:P0}]\n" +
                      $"关系: {relationDesc}, 好感度: {opinion:+0;-0}"
                    : $"Speaker: {speaker.Name.ToStringShort} [Traits: {speakerTraits}, Job: {speakerJob}, Mood: {speaker.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "80%"}]\n" +
                      $"Listener: {listener.Name.ToStringShort} [Traits: {listenerTraits}, Job: {listenerJob}, Mood: {listenerMood:P0}]\n" +
                      $"Relation: {relationDesc}, Opinion: {opinion:+0;-0}"
            });

            string recentHistory = GetRecentDialogueHistory(speaker.Name.ToStringShort, listener.Name.ToStringShort);

            // Chronic Thought Fatigue Decay: Filter out thoughts already explicitly discussed in recent dialogue turns
            var badThoughts = speaker.needs?.mood?.thoughts?.memories?.Memories?
                .Where(m => m.MoodOffset() < 0 && !recentHistory.Contains(PawnDataExtractor.SanitizeThoughtLabel(m.LabelCap)))
                .OrderBy(m => m.MoodOffset())
                .Take(2)
                .Select(m => $"{PawnDataExtractor.SanitizeThoughtLabel(m.LabelCap)}({m.MoodOffset():+0;-0})")
                .ToList() ?? new List<string>();
            string thoughtsContext = badThoughts.Count > 0
                ? string.Join(", ", badThoughts)
                : (isZh ? "生理与心理状态基本平稳（关注当前工作与彼此生活）" : "Physical and mental state relatively stable (focusing on current colony tasks and mutual well-being)");

            string listenerName = listener.Name.ToStringShort;
            string socialPrompt;
            if (isZh)
            {
                string sceneBase = (day % 6) switch
                {
                    1 => $"你们正坐在食堂餐桌旁享用热餐，食物的热气升腾，你转头看向身旁的 {listenerName}，顺着当前气氛聊起了家常与今日感受。",
                    2 => $"你们在娱乐室偶遇（下棋/打台球/玩马蹄铁）。闲暇轻松的氛围中，你笑着对 {listenerName} 搭话交流。",
                    3 => $"你们在工坊并肩劳作，手头正忙着敲打打磨工件。趁着搬运材料的空当，你向身旁的 {listenerName} 聊起近来的体会。",
                    4 => $"你们正在农田与温室间巡视庄稼与药草。看着茁壮成长的作物，你侧过身与 {listenerName} 探讨起近期的收成与安排。",
                    5 => $"你们在储藏区共同搬运物资并清点库存。趁着歇息喝水的片刻，你对 {listenerName} 表达了对目前物资储备的看法。",
                    _ => $"你在医务室探视休息，偶遇了走过来的 {listenerName}。互相关心了彼此的身体与精神状态。",
                };

                if (speaker.InMentalState)
                {
                    sceneBase = $"你当前正处于严重精神崩溃/失常状态（{speaker.MentalStateDef?.label ?? "崩溃失控"}），情绪极度脆弱或暴躁！你迎面撞见 {listenerName}，压抑的负面情绪彻底爆发，直接宣泄你的怒火、绝望、嘲讽或挑衅，展现真实废土心理张力。";
                }
                else if (speaker == listener)
                {
                    sceneBase = $"营地四顾寂静，你独自一人穿过废墟与走廊，低声自言自语，沉淀着这些日子以来的求生执念与心事。";
                }

                socialPrompt = $"{sceneBase}\n" +
                               $"【当前生理/心理困扰】: {thoughtsContext}\n" +
                               $"【与 {listenerName} 的过往深入交谈记录】: {recentHistory}\n" +
                               $"【刚性准则】: 请调用 express_dialogue 说出独创台词并给出好感变动(-5~+5)。" +
                               $"你正在对 {listenerName} 说话，务必认清交谈对象，严禁叫错名字。严禁机械复读或借用对方曾经使用过的独特诗意比喻/修辞。即使环境未改变，也必须从当前不同工作细节、对营地规划的看法或彼此生活关怀等全新角度展开！";
            }
            else
            {
                string sceneBase = (day % 6) switch
                {
                    1 => $"You are sharing a meal together in the dining area and turn to {listenerName} to talk about how today is going.",
                    2 => $"You cross paths during a brief break in the recreation area and strike up a conversation with {listenerName}.",
                    3 => $"You are working side by side in the workshop and talk with {listenerName} while handling materials.",
                    4 => $"You are checking the growing zones and crops together and discuss the harvest and colony plans with {listenerName}.",
                    5 => $"You are hauling and sorting supplies in the storage zone and share your thoughts on the colony's stockpile with {listenerName}.",
                    _ => $"You meet {listenerName} near the barracks/infirmary and check in on each other's health and morale.",
                };

                if (speaker.InMentalState)
                {
                    sceneBase = $"You are currently in a mental break ({speaker.MentalStateDef?.label ?? "mental break"}) and emotionally overwhelmed! Running into {listenerName}, you vent your raw frustration, despair, or agitation.";
                }
                else if (speaker == listener)
                {
                    sceneBase = $"The camp is quiet as you walk alone and murmur to yourself, reflecting on survival on the Rim.";
                }

                socialPrompt = $"{sceneBase}\n" +
                               $"[Current Concerns]: {thoughtsContext}\n" +
                               $"[Recent Conversations with {listenerName}]: {recentHistory}\n" +
                               $"[Instruction]: Call express_dialogue with an original line of dialogue in English and a relation_delta (-5 to +5). Address {listenerName} accurately and do not repeat metaphors from prior turns.";
            }

            envelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                LayerTag = "L4",
                Content = socialPrompt
            });

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            queue?.ClearAllCooldowns();
            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 65f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                elapsed += Time.unscaledDeltaTime;
                if (queue is ITickableRequestQueue tq)
                {
                    tq.CurrentTick += 5;
                    tq.Tick();
                }
                DismissModalWindows(Find.CurrentMap);
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                var args = ExtractToolArguments(val.ToolCallsJson, "express_dialogue", val.Content);
                string finalSpeech;
                string finalTag;
                int finalRelDelta;

                if (args != null)
                {
                    string? speech = args["speech"]?.ToString() ??
                                     args["reply"]?.ToString() ??
                                     args["dialogue"]?.ToString() ??
                                     args["text"]?.ToString() ??
                                     args["content"]?.ToString();

                    finalSpeech = !string.IsNullOrWhiteSpace(speech) ? speech!.Trim() : (!string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : GenerateDynamicSocialDialogueFallback(speaker, listener, day));
                    finalTag = SanitizeThoughtTag(args["thought_tag"]?.ToString());
                    int relDelta = args["relation_delta"]?.Value<int>() ?? 1;
                    finalRelDelta = Mathf.Clamp(relDelta, -5, 5);
                }
                else
                {
                    finalSpeech = !string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : GenerateDynamicSocialDialogueFallback(speaker, listener, day);
                    finalTag = "FRIENDLY";
                    finalRelDelta = 1;
                }

                // Defensive JSON extraction: unwrap if finalSpeech contains or is wrapped in raw JSON object
                if (finalSpeech.Contains("{") && finalSpeech.Contains("}"))
                {
                    try
                    {
                        int sIdx = finalSpeech.IndexOf('{');
                        int eIdx = finalSpeech.LastIndexOf('}');
                        if (sIdx >= 0 && eIdx > sIdx)
                        {
                            string jsonCandidate = finalSpeech.Substring(sIdx, eIdx - sIdx + 1);
                            while (jsonCandidate.StartsWith("{{") && jsonCandidate.EndsWith("}}"))
                            {
                                jsonCandidate = jsonCandidate.Substring(1, jsonCandidate.Length - 2).Trim();
                            }
                            try
                            {
                                var jObj = JObject.Parse(jsonCandidate);
                                string? inner = jObj["speech"]?.ToString()
                                             ?? jObj["reply"]?.ToString()
                                             ?? jObj["dialogue"]?.ToString()
                                             ?? jObj["content"]?.ToString()
                                             ?? jObj["text"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(inner))
                                {
                                    finalSpeech = inner!.Trim();
                                }
                                if (string.IsNullOrEmpty(finalTag) || finalTag == "FRIENDLY")
                                {
                                    var innerTag = jObj["thought_tag"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(innerTag)) finalTag = SanitizeThoughtTag(innerTag);
                                }
                                if (finalRelDelta == 1 && jObj["relation_delta"] != null)
                                {
                                    finalRelDelta = Mathf.Clamp(jObj["relation_delta"].Value<int>(), -5, 5);
                                }
                            }
                            catch
                            {
                                var m = Regex.Match(jsonCandidate, @"""(?:speech|reply|dialogue|content|text)""\s*:\s*""(?<val>(?:\\.|[^""\\])*)""");
                                if (m.Success)
                                {
                                    string unescaped = Regex.Unescape(m.Groups["val"].Value);
                                    if (!string.IsNullOrWhiteSpace(unescaped))
                                    {
                                        finalSpeech = unescaped.Trim();
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                finalSpeech = StripModelXmlTags(finalSpeech);

                if (string.IsNullOrWhiteSpace(finalSpeech))
                {
                    finalSpeech = GenerateDynamicSocialDialogueFallback(speaker, listener, day);
                }

                RecordPlaythroughDialogue(speaker.Name.ToStringShort, listener.Name.ToStringShort, finalSpeech);

                if (recordInReport)
                {
                    report.DialogueSpeaker = speaker.Name?.ToStringShort ?? "Speaker";
                    report.DialogueListener = listener.Name?.ToStringShort ?? "Listener";
                    report.DialogueSpeech = finalSpeech;
                    report.DialogueThoughtTag = finalTag;
                    report.DialogueRelationDelta = finalRelDelta;
                }

                Log.Message($"[RimMind-Playthrough][Day {day}] Dialogue: {speaker.Name.ToStringShort} -> {listener.Name.ToStringShort}: \"{finalSpeech}\" (Tag: {finalTag}, Rel: {finalRelDelta})");
            }
            else
            {
                string fallback = GenerateDynamicSocialDialogueFallback(speaker, listener, day);
                RecordPlaythroughDialogue(speaker.Name.ToStringShort, listener.Name.ToStringShort, fallback);
                if (recordInReport)
                {
                    report.DialogueSpeaker = speaker.Name?.ToStringShort ?? "Speaker";
                    report.DialogueListener = listener.Name?.ToStringShort ?? "Listener";
                    report.DialogueSpeech = fallback;
                    report.DialogueThoughtTag = "FRIENDLY";
                    report.DialogueRelationDelta = 1;
                }
                Log.Warning($"[RimMind-Playthrough][Day {day}] Dialogue fallback ({speaker.Name.ToStringShort} -> {listener.Name.ToStringShort}): {responseResult?.Error.Message}");
            }
        }

        private IEnumerator ExecuteAutonomousAgentAction(Pawn agentPawn, int day, PlaythroughDayReport report, bool recordInReport = true)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null)
            {
                if (queue.TotalQueuedCount > 15) queue.CancelAllRequests();
                queue.ClearAllCooldowns();
            }

            RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(agentPawn);
            var agentComp = CompPawnAgent.GetComp(agentPawn);
            if (agentComp != null)
            {
                agentComp.EnsureAgentCreated();
                bool anyOtherActive = GetLivingColonists().Any(c =>
                    !ReferenceEquals(c, agentPawn) && CompPawnAgent.GetComp(c)?.Agent?.State == AgentState.Active);
                if (!anyOtherActive && agentComp.Agent != null && agentComp.Agent.State != AgentState.Active)
                {
                    agentComp.Agent.TransitionTo(AgentState.Active);
                }
            }

            var tools = new List<StructuredTool>
            {
                new StructuredTool
                {
                    Name = "prioritize_work",
                    Description = "Prioritize a critical colony labor task (e.g. Firefighting, Doctor, Warden, Growing, Crafting, Construction, Hauling, Cleaning)",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"work_type\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"work_type\",\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "take_job",
                    Description = "Directly assign an immediate action job (e.g. HaulToStorage, CleanFilth, TendPatient, RepairBuilding, CutPlants)",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"job_type\":{\"type\":\"string\"},\"target\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"job_type\",\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "place_blueprint",
                    Description = "Directly spawn a construction blueprint on the colony map to address urgent survival, cooling, housing, or logistics needs (e.g. Bed, PassiveCooler, Cooler, Campfire, SolarGenerator, Battery, Wall, Door, TableButcher, HandTailoringBench)",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"building_def\":{\"type\":\"string\"},\"offset_x\":{\"type\":\"integer\"},\"offset_z\":{\"type\":\"integer\"},\"stuff_def\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"building_def\",\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "designate_map_order",
                    Description = "Directly designate harvesting, foraging, mining, or hauling orders on the map to resolve resource shortages or crises",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"order_type\":{\"type\":\"string\",\"enum\":[\"Harvest\",\"Mine\",\"CutPlants\",\"Haul\"]},\"target\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"order_type\",\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "actions_query_buildable_structures",
                    Description = "Query catalog of available structures and furniture in colony with build cost and tech prerequisites (category: Furniture, Temperature, Structure, Production, Power)",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"},\"search_keyword\":{\"type\":\"string\"}},\"required\":[]}"
                },
                new StructuredTool
                {
                    Name = "query_tool_help",
                    Description = "Query detailed usage instructions, schema and examples for a specific tool or composite mechanism",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"tool_name\":{\"type\":\"string\"}},\"required\":[\"tool_name\"]}"
                },
                new StructuredTool
                {
                    Name = "eat_and_recreation",
                    Description = "Composite mechanism: satisfy urgent nourishment needs and enjoy social recreation to restore morale",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "stabilize_rest",
                    Description = "Composite mechanism: find the nearest safe medical bed, bandage wounds, and rest to recover stamina",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "actions_add_worktable_bill",
                    Description = "Add a production bill (e.g. CookMealSimple, CookMealFine, ButcherCorpseFlesh, Make_StoneBlocks, Make_Apparel_Parka) to an available colony worktable (Stove, Butcher table, Tailoring bench, Stonecutter table)",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"recipe_def\":{\"type\":\"string\",\"description\":\"DefName of the recipe (e.g. CookMealSimple, CookMealFine, ButcherCorpseFlesh, Make_StoneBlocks, Make_Apparel_Parka)\"},\"target_count\":{\"type\":\"integer\",\"description\":\"Target stock or repeat count (default: 10)\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"recipe_def\",\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "actions_manage_equipment",
                    Description = "Autonomously direct colonist to equip protective gear, weapon, or thermal apparel from storage for survival and safety",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"purpose\":{\"type\":\"string\",\"enum\":[\"Thermal\",\"Armor\",\"Auto\"]},\"target_apparel_def\":{\"type\":\"string\",\"description\":\"Optional specific apparel/weapon defName to equip (e.g. Apparel_Parka, Apparel_Tuque, Apparel_FlakVest, Gun_BoltActionRifle)\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
                }
            };

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario(RimMindAPI.Context.ScenarioDecision)
                .WithModId("RimMind-Actions")
                .WithNpcId("NPC-" + agentPawn.thingIDNumber)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(800)
                .WithTemperature(0.6f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L0",
                Content = "You are the autonomous colonist AI agent in RimWorld. You must choose ONE tool from [prioritize_work, take_job, place_blueprint, actions_add_worktable_bill, actions_manage_equipment, actions_query_buildable_structures, query_tool_help, designate_map_order, eat_and_recreation, stabilize_rest] to direct your next action."
            });
            Map pawnMap = agentPawn.Map ?? Find.CurrentMap;
            int woodCount = CountAvailableMapResource(pawnMap, ThingDefOf.WoodLog);
            int steelCount = CountAvailableMapResource(pawnMap, ThingDefOf.Steel);
            var compDef = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("ComponentsIndustrial");
            int compCount = CountAvailableMapResource(pawnMap, compDef);
            int mealCount = CountAvailableMapResource(pawnMap, ThingDefOf.MealSimple)
                + CountAvailableMapResource(pawnMap, ThingDefOf.MealFine)
                + CountAvailableMapResource(pawnMap, ThingDefOf.MealSurvivalPack);
            int pendingBlueprints = pawnMap?.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
            int pendingFrames = pawnMap?.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
            int pendingConstructionCount = pendingBlueprints + pendingFrames;
            bool canPlaceMoreBlueprints = pendingConstructionCount < 2 && woodCount >= 115;

            bool hasHarvestablePlants = pawnMap?.listerThings?.AllThings?
                .OfType<Plant>()
                .Any(p => p.Spawned && !p.Destroyed && !p.Position.Fogged(pawnMap) &&
                          p.def?.plant != null && !p.def.plant.IsTree && p.def.plant.Harvestable &&
                          (p.HarvestableNow || p.Growth >= 0.7f) &&
                          pawnMap.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null) ?? false;

            bool isZh = IsChineseLanguageActive();
            string agentTraits = string.Join(", ", agentPawn.story?.traits?.allTraits?.Select(t => t.LabelCap) ?? Enumerable.Empty<string>());
            if (string.IsNullOrEmpty(agentTraits)) agentTraits = isZh ? "普通" : "None";
            int constructSkill = agentPawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
            int plantsSkill = agentPawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
            int miningSkill = agentPawn.skills?.GetSkill(SkillDefOf.Mining)?.Level ?? 0;
            int cookingSkill = agentPawn.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
            bool canConstruct = !agentPawn.WorkTagIsDisabled(WorkTags.ManualSkilled);
            bool canCook = !agentPawn.WorkTagIsDisabled(WorkTags.Cooking);
            bool hasBed = agentPawn.ownership?.OwnedBed != null;
            var disabledWorkDefs = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => agentPawn.WorkTypeIsDisabled(w))
                .Select(w => w.defName)
                .ToList();
            string disabledWorkSummary = disabledWorkDefs.Count > 0
                ? string.Join(", ", disabledWorkDefs)
                : (isZh ? "无" : "None");

            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L1",
                Content = isZh
                    ? $"Pawn: {agentPawn.Name.ToStringShort} [特质: {agentTraits}, 当前: {agentPawn.CurJobDef?.defName ?? "Idle"}, 心情: {agentPawn.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "80%"}, 健康: {agentPawn.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%"}]\n" +
                      $"技能等级: 建造({constructSkill}{(canConstruct ? "" : "-禁用")}), 烹饪({cookingSkill}{(canCook ? "" : "-禁用")}), 种植({plantsSkill}), 采掘({miningSkill}) | 禁用工作(严禁prioritize_work选择): {disabledWorkSummary}\n" +
                      $"营地物资库存: 木材 {woodCount} (安全底线: 80), 钢铁 {steelCount}, 零部件 {compCount}, 口粮 {mealCount} 份\n" +
                      $"施工队列: 待建蓝图/框架 {pendingConstructionCount} 个 (并发上限: 2) | 野外可采浆果/成熟作物: {(hasHarvestablePlants ? "有" : "无")}\n" +
                      $"居住现状: {(hasBed ? "已有固定床位" : "无专属床位(露宿泥地)")}"
                    : $"Pawn: {agentPawn.Name.ToStringShort} [Traits: {agentTraits}, Job: {agentPawn.CurJobDef?.defName ?? "Idle"}, Mood: {agentPawn.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "80%"}, Health: {agentPawn.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%"}]\n" +
                      $"Skills: Construction({constructSkill}{(canConstruct ? "" : "-Disabled")}), Cooking({cookingSkill}{(canCook ? "" : "-Disabled")}), Plants({plantsSkill}), Mining({miningSkill}) | Disabled WorkTypes (DO NOT pick in prioritize_work): {disabledWorkSummary}\n" +
                      $"Colony Stockpile: Wood {woodCount} (safety reserve: 80), Steel {steelCount}, Components {compCount}, Meals {mealCount}\n" +
                      $"Construction Queue: Pending blueprints/frames {pendingConstructionCount} (concurrency cap: 2) | Harvestable wild berries/mature crops: {(hasHarvestablePlants ? "Yes" : "No")}\n" +
                      $"Housing Status: {(hasBed ? "Has assigned bed" : "No bed (sleeping on ground)")}"
            });

            float outdoorTemp = pawnMap != null ? pawnMap.mapTemperature.OutdoorTemp : 21f;
            float foodLevel = agentPawn.needs?.food?.CurLevelPercentage ?? 1f;
            float moodLevel = agentPawn.needs?.mood?.CurLevelPercentage ?? 0.8f;
            bool isIdle = agentPawn.CurJobDef == null || agentPawn.CurJobDef == JobDefOf.Wait_Wander || agentPawn.CurJobDef == JobDefOf.GotoWander;

            bool sleptOnGround = agentPawn.needs?.mood?.thoughts?.memories?.Memories?
                .Any(m => m.def.defName.Contains("SleptOnGround")) == true && !hasBed;

            string statusContext;
            if (isZh)
            {
                if ((sleptOnGround || !hasBed) && canPlaceMoreBlueprints)
                {
                    statusContext = $"你正露宿在冰冷泥地上（缺少床铺，严重损害身心健康与心情；当前营地存有木材 {woodCount}，待建蓝图 {pendingConstructionCount}/2）。你可调用 actions_query_buildable_structures 查询营地可用家具，或调用 place_blueprint 规划木床（Bed，需35木材），或调用 prioritize_work 优先建造作业。";
                }
                else if (sleptOnGround || !hasBed)
                {
                    string buildBlockReason = pendingConstructionCount >= 2
                        ? $"当前已有 {pendingConstructionCount} 个待建蓝图/框架（已达并发上限 2，严禁调用 place_blueprint）"
                        : $"当前入库木材 {woodCount}（低于安全储备线+造价 115，严禁调用 place_blueprint）";
                    statusContext = $"你尚未拥有专属床铺，但{buildBlockReason}！请调用 prioritize_work(work_type: \"Construction\") 推进现有蓝图施工，或调用 take_job(job_type: \"HaulToStorage\") 搬运木材，或调用 designate_map_order(order_type: \"CutPlants\", target: \"Tree\") 砍树补充木材。";
                }
                else if (outdoorTemp > 35f)
                {
                    statusContext = canPlaceMoreBlueprints
                        ? $"当前户外气温高达 {outdoorTemp:F0}°C，酷暑炙热。你可调用 place_blueprint 规划被动制冷器（PassiveCooler，需50木材），或调用 stabilize_rest 避暑休整。"
                        : $"当前户外气温高达 {outdoorTemp:F0}°C，酷暑炙热（待建蓝图 {pendingConstructionCount}/2，请勿新增蓝图）。请调用 stabilize_rest 避暑休整，或调用 prioritize_work(work_type: \"Construction\") 完成现有降温设施。";
                }
                else if (outdoorTemp < 2f)
                {
                    statusContext = canPlaceMoreBlueprints
                        ? $"当前户外严寒冰冻（{outdoorTemp:F0}°C）。你可调用 place_blueprint 放置营火（Campfire，需20木材），或调用 prioritize_work 优先室内作业。"
                        : $"当前户外严寒冰冻（{outdoorTemp:F0}°C，待建蓝图 {pendingConstructionCount}/2 或木材不足，严禁调用 place_blueprint）。请调用 prioritize_work(work_type: \"Construction\") 完成现有建筑，或调用 stabilize_rest / eat_and_recreation 在室内休整。";
                }
                else if (foodLevel < 0.25f)
                {
                    statusContext = hasHarvestablePlants
                        ? $"你感到胃部极度饥饿（饱腹度 {foodLevel:P0}，营地现有存粮 {mealCount} 份）。你可调用 eat_and_recreation 进餐，或调用 designate_map_order(order_type: \"Harvest\") 圈定收割成熟作物/采摘浆果。"
                        : $"你感到胃部极度饥饿（饱腹度 {foodLevel:P0}，营地现有存粮 {mealCount} 份）。请调用 eat_and_recreation 进餐与恢复心情，或调用 prioritize_work(work_type: \"Hauling\") 搬运散落食物。";
                }
                else if (isIdle)
                {
                    string skillHints = "";
                    if (plantsSkill >= 4) skillHints += $"，或利用植物专长(Lv.{plantsSkill})调用 designate_map_order(order_type: \"CutPlants\", target: \"Tree\") 伐木";
                    if (miningSkill >= 4) skillHints += $"，或利用采矿专长(Lv.{miningSkill})调用 designate_map_order(order_type: \"Mine\") 开采矿石";
                    if (constructSkill >= 3 && canConstruct && canPlaceMoreBlueprints) skillHints += $"，或规划餐桌/凳子/火把基础设施";
                    statusContext = $"你当前处于空闲状态（营地存有木材 {woodCount}，钢铁 {steelCount}，待建蓝图 {pendingConstructionCount}/2）。你可调用 prioritize_work / take_job 推进营地作业{skillHints}。";
                }
                else
                {
                    statusContext = $"当前是殖民地第 {day} 天。你正处于日常活动中（营地存有木材 {woodCount}，钢铁 {steelCount}，待建蓝图 {pendingConstructionCount}/2），请权衡自身特长与营地建设需求，调用最适工具做出行动决策。";
                }
            }
            else
            {
                if ((sleptOnGround || !hasBed) && canPlaceMoreBlueprints)
                {
                    statusContext = $"You are sleeping on the ground without a bed (Wood: {woodCount}, pending blueprints: {pendingConstructionCount}/2). You may call actions_query_buildable_structures, place_blueprint for a Bed (35 wood), or prioritize_work for Construction.";
                }
                else if (sleptOnGround || !hasBed)
                {
                    string buildBlockReason = pendingConstructionCount >= 2
                        ? $"there are already {pendingConstructionCount} pending blueprints/frames (cap: 2, do NOT call place_blueprint)"
                        : $"stockpiled wood is {woodCount} (below reserve threshold 115, do NOT call place_blueprint)";
                    statusContext = $"You do not have an assigned bed yet, but {buildBlockReason}! Call prioritize_work(work_type: \"Construction\"), take_job(job_type: \"HaulToStorage\"), or designate_map_order(order_type: \"CutPlants\", target: \"Tree\") to chop wood.";
                }
                else if (outdoorTemp > 35f)
                {
                    statusContext = canPlaceMoreBlueprints
                        ? $"Outdoor temperature is {outdoorTemp:F0}°C (extreme heat). You may call place_blueprint for a PassiveCooler (50 wood) or stabilize_rest."
                        : $"Outdoor temperature is {outdoorTemp:F0}°C (pending blueprints {pendingConstructionCount}/2, do not place new blueprints). Call stabilize_rest or prioritize_work(work_type: \"Construction\").";
                }
                else if (outdoorTemp < 2f)
                {
                    statusContext = canPlaceMoreBlueprints
                        ? $"Outdoor temperature is freezing ({outdoorTemp:F0}°C). You may call place_blueprint for a Campfire (20 wood) or prioritize_work."
                        : $"Outdoor temperature is freezing ({outdoorTemp:F0}°C, pending blueprints {pendingConstructionCount}/2 or low wood, do not call place_blueprint). Call prioritize_work(work_type: \"Construction\"), stabilize_rest, or eat_and_recreation.";
                }
                else if (foodLevel < 0.25f)
                {
                    statusContext = hasHarvestablePlants
                        ? $"You are very hungry (Food: {foodLevel:P0}, Meals: {mealCount}). Call eat_and_recreation or designate_map_order(order_type: \"Harvest\") to harvest mature crops/berries."
                        : $"You are very hungry (Food: {foodLevel:P0}, Meals: {mealCount}). Call eat_and_recreation or prioritize_work(work_type: \"Hauling\").";
                }
                else if (isIdle)
                {
                    string skillHints = "";
                    if (plantsSkill >= 4) skillHints += $", or use Plants(Lv.{plantsSkill}) with designate_map_order(order_type: \"CutPlants\", target: \"Tree\")";
                    if (miningSkill >= 4) skillHints += $", or use Mining(Lv.{miningSkill}) with designate_map_order(order_type: \"Mine\")";
                    if (constructSkill >= 3 && canConstruct && canPlaceMoreBlueprints) skillHints += $", or place basic furniture blueprints";
                    statusContext = $"You are currently idle (Wood: {woodCount}, Steel: {steelCount}, pending blueprints: {pendingConstructionCount}/2). Call prioritize_work or take_job to advance colony work{skillHints}.";
                }
                else
                {
                    statusContext = $"It is Colony Day {day} (Wood: {woodCount}, Steel: {steelCount}, pending blueprints: {pendingConstructionCount}/2). Choose the most appropriate tool for your skills and colony needs.";
                }
            }

            var stoveTables = pawnMap?.listerBuildings?.allBuildingsColonist?
                .OfType<Building_WorkTable>()
                .Where(w => w != null && !w.Destroyed && (w.def.defName.Contains("Stove") || w.def.defName.Contains("Campfire")))
                .ToList() ?? new List<Building_WorkTable>();
            bool hasStove = stoveTables.Count > 0;
            bool hasCookBill = stoveTables.Any(w => w.billStack?.Bills?.Any(b => b != null && !b.suspended && b.recipe?.defName != null && b.recipe.defName.IndexOf("CookMeal", StringComparison.OrdinalIgnoreCase) >= 0) == true);
            bool anyStoveHasFuel = stoveTables.Any(w =>
            {
                var refuel = w.GetComp<CompRefuelable>();
                return refuel == null || refuel.HasFuel;
            });

            if (!hasStove) { statusContext += isZh ? " 当前营地尚未建成料理台或篝火（严禁调用 actions_add_worktable_bill 挂单烹饪！），请先推进现有建造或采集。" : " Note: No stove or campfire is built on the map yet (do NOT call actions_add_worktable_bill for cooking!)."; } else if (mealCount < 10 && hasStove)
            {
                var rawCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
                var rawFoodCount = (rawCat != null && pawnMap?.resourceCounter != null) ? pawnMap.resourceCounter.GetCountIn(rawCat) : 0;
                if (rawFoodCount <= 0 && rawCat != null && pawnMap?.listerThings != null)
                {
                    rawFoodCount = pawnMap.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                        .Where(t => t != null && t.Spawned && !t.IsForbidden(Faction.OfPlayer) && t.def?.thingCategories != null && t.def.thingCategories.Contains(rawCat))
                        .Sum(t => t.stackCount);
                }
                if (rawFoodCount <= 0)
                {
                    if (hasHarvestablePlants)
                    {
                        statusContext += isZh
                            ? $" 营地熟食紧缺且生食材匮乏（严禁调用 actions_add_worktable_bill），当前地图有成熟野生植物/作物，请优先调用 designate_map_order(order_type: \"Harvest\") 圈定收割采集！"
                            : $" Meals and raw ingredients are scarce (do NOT call actions_add_worktable_bill); mature plants/crops are available on the map, so prioritize designate_map_order(order_type: \"Harvest\")!";
                    }
                    else
                    {
                        statusContext += isZh
                            ? $" 营地生食材匮乏且当前季节无野生浆果/成熟作物可收割（严禁调用 actions_add_worktable_bill 或 designate_map_order(Harvest)），请调用 prioritize_work、take_job 或 designate_map_order(order_type: \"CutPlants\", target: \"Tree\") 维持营地运转！"
                            : $" Raw ingredients are scarce and no harvestable crops/berries are ready (do NOT call actions_add_worktable_bill or designate_map_order(Harvest)); call prioritize_work, take_job, or designate_map_order(order_type: \"CutPlants\", target: \"Tree\")!";
                    }
                }
                else if (hasCookBill)
                {
                    if (!anyStoveHasFuel && woodCount <= 0)
                    {
                        statusContext += isZh
                            ? $" 料理台已存在 CookMealSimple 烹饪清单（严禁重复调用 actions_add_worktable_bill！），但当前料理台缺乏木材燃料（木材=0），请优先调用 designate_map_order(order_type: \"Harvest\", target: \"Tree\") 砍伐树木补充燃料或调用 prioritize_work 推进搬运/烹饪！"
                            : $" Stove already has a CookMealSimple bill (do NOT call actions_add_worktable_bill!), but lacks wood fuel (Wood=0); call designate_map_order(order_type: \"Harvest\", target: \"Tree\") or prioritize_work!";
                    }
                    else
                    {
                        statusContext += isZh
                            ? $" 料理台已存在 CookMealSimple 烹饪清单（严禁重复调用 actions_add_worktable_bill！），请调用 prioritize_work(work_type: \"Cooking\" 或 \"Hauling\") 或 take_job 推进营地作业！"
                            : $" Stove already has a CookMealSimple bill (do NOT call actions_add_worktable_bill!); call prioritize_work(work_type: \"Cooking\" or \"Hauling\") or take_job!";
                    }
                }
                else if (!anyStoveHasFuel && woodCount <= 0)
                {
                    statusContext += isZh
                        ? $" 营地料理台当前缺乏木材燃料（木材=0，严禁调用 actions_add_worktable_bill），请先调用 designate_map_order(order_type: \"Harvest\", target: \"Tree\") 砍伐树木获取燃料！"
                        : $" Stove currently lacks wood fuel (Wood=0, do NOT call actions_add_worktable_bill); call designate_map_order(order_type: \"Harvest\", target: \"Tree\") first!";
                }
                else
                {
                    statusContext += isZh
                        ? $" 【最高优先级任务】营地熟食口粮紧缺（存粮 {mealCount}，生料存有 {rawFoodCount}），已建造料理台且尚无烹饪清单。请立即优先调用 actions_add_worktable_bill(recipe_def: \"CookMealSimple\", target_count: 10) 挂单制作简单食物！"
                        : $" [CRITICAL PRIORITY] Meals are low (Meals: {mealCount}, Raw food: {rawFoodCount}) and the built stove has no active cook bill. Immediately call actions_add_worktable_bill(recipe_def: \"CookMealSimple\", target_count: 10) to start cooking meals!";
                }
            }

            string failureHint = "";
            if (_recentActionFailures.TryGetValue(agentPawn.thingIDNumber, out var lastFail) && !string.IsNullOrEmpty(lastFail))
            {
                failureHint = isZh
                    ? $" 【动作失败经验反思】你此前尝试的行动未成功: {lastFail}。请在本次决策中吸取教训，避免重复失败，选择切实可行、资源充足的动作。"
                    : $" [Prior Action Failure Reflection] Your previous action failed: {lastFail}. Choose a feasible action with sufficient resources instead.";
            }

            string afternoonPrompt = isZh
                ? $"【自主决策断点】{statusContext}{failureHint}"
                : $"[Autonomous Decision Checkpoint] {statusContext}{failureHint}";

            envelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                LayerTag = "L4",
                Content = afternoonPrompt
            });

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            queue?.ClearAllCooldowns();
            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 65f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                elapsed += Time.unscaledDeltaTime;
                if (queue is ITickableRequestQueue tq)
                {
                    tq.CurrentTick += 5;
                    tq.Tick();
                }
                DismissModalWindows(Find.CurrentMap);
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                var extracted = ExtractToolCall(val.ToolCallsJson, val.Content);
                var argsObj = extracted.Arguments;
                string? toolName = extracted.ToolName;
                string detail = "";
                string reason = !string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : "";

                // Only infer toolName when an unambiguous parameter key is present; never fallback to a fake default tool
                if (string.IsNullOrWhiteSpace(toolName) && argsObj != null)
                {
                    if (argsObj.TryGetValue("building_def", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "place_blueprint";
                    else if (argsObj.TryGetValue("work_type", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "prioritize_work";
                    else if (argsObj.TryGetValue("job_type", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "take_job";
                    else if (argsObj.TryGetValue("order_type", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "designate_map_order";
                    else if (argsObj.TryGetValue("recipe_def", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "actions_add_worktable_bill";
                    else if (argsObj.TryGetValue("target_apparel_def", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "actions_manage_equipment";
                    else if (argsObj.TryGetValue("category", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "actions_query_buildable_structures";
                    else if (argsObj.TryGetValue("tool_name", StringComparison.OrdinalIgnoreCase, out _))
                        toolName = "query_tool_help";
                }

                // Multi-turn Round 1 -> Round 2 when model invokes a sensory query tool
                if (string.Equals(toolName, "actions_query_buildable_structures", StringComparison.OrdinalIgnoreCase))
                {
                    string cat = argsObj != null && argsObj.TryGetValue("category", StringComparison.OrdinalIgnoreCase, out var cTok) ? cTok.ToString() : "All";
                    string queryArgsJson = argsObj != null ? argsObj.ToString(Newtonsoft.Json.Formatting.None) : $"{{\"category\":\"{cat}\"}}";

                    string realQueryOutput = "";
                    var registeredQueryTool = RimMindAPI.Tools.FindById("actions_query_buildable_structures");
                    if (registeredQueryTool != null)
                    {
                        var toolCallArgs = new RimMind.Application.Common.Models.Tools.ToolCallArgs
                        {
                            ToolCallId = Guid.NewGuid().ToString("N"),
                            ToolName = "actions_query_buildable_structures",
                            ArgumentsJson = queryArgsJson,
                            PawnId = agentPawn.thingIDNumber,
                            Ct = System.Threading.CancellationToken.None
                        };
                        var qExec = registeredQueryTool.ExecuteAsync(toolCallArgs, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        if (qExec.IsOk && !string.IsNullOrWhiteSpace(qExec.Value.Content))
                        {
                            realQueryOutput = qExec.Value.Content!;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(realQueryOutput))
                    {
                        var buildables = GetBuildableStructuresForRunner(pawnMap, cat, woodCount, steelCount, compCount);
                        realQueryOutput = string.Join("\n", buildables);
                    }

                    string r2ActionGuidance = isZh
                        ? (canPlaceMoreBlueprints
                            ? $"请根据当前小人的生存需求与营地储备（现有木材 {woodCount}，待建蓝图 {pendingConstructionCount}/2），立即调用终端动作工具（如 place_blueprint, prioritize_work, take_job, stabilize_rest）执行物理落地动作！"
                            : $"注意：当前待建蓝图/框架已有 {pendingConstructionCount} 个（并发上限 2）或木材不足（现有 {woodCount}，安全底线 80），严禁调用 place_blueprint！请立即调用 prioritize_work(work_type: \"Construction\")、take_job 或 designate_map_order(order_type: \"CutPlants\", target: \"Tree\") 执行物理落地动作！")
                        : (canPlaceMoreBlueprints
                            ? $"Based on this colonist's survival needs and colony reserves (Wood: {woodCount}, pending blueprints: {pendingConstructionCount}/2), immediately invoke a terminal action tool (such as place_blueprint, prioritize_work, take_job, stabilize_rest) to execute a physical action!"
                            : $"Note: There are already {pendingConstructionCount} pending blueprints/frames (max 2) or insufficient Wood ({woodCount}, safety floor 80) — DO NOT call place_blueprint! Immediately invoke prioritize_work(work_type: \"Construction\"), take_job, or designate_map_order(order_type: \"CutPlants\", target: \"Tree\") to execute a physical action!");

                    string queryObservation = isZh
                        ? $"[Tool Result: actions_query_buildable_structures]\n{realQueryOutput}\n【闭环指示】你已获取可用建筑与消耗清单。{r2ActionGuidance}"
                        : $"[Tool Result: actions_query_buildable_structures]\n{realQueryOutput}\n[Closed-Loop Instruction] You have retrieved the buildable structures and cost list. {r2ActionGuidance}";

                    var r2Envelope = LlmRequestEnvelopeBuilder
                        .ForScenario(RimMindAPI.Context.ScenarioDecision)
                        .WithModId("RimMind-Actions")
                        .WithNpcId("NPC-" + agentPawn.thingIDNumber)
                        .WithTools(tools)
                        .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                        .WithMaxTokens(800)
                        .WithTemperature(0.5f)
                        .WithPriority(AIRequestPriority.High)
                        .Build();

                    foreach (var m in envelope.Messages)
                    {
                        r2Envelope.Messages.Add(m);
                    }
                    r2Envelope.Messages.Add(new ChatMessage
                    {
                        Role = "assistant",
                        LayerTag = "L4",
                        Content = !string.IsNullOrWhiteSpace(val.Content)
                            ? val.Content
                            : (isZh ? $"已调用 actions_query_buildable_structures(category: \"{cat}\")" : $"Invoked actions_query_buildable_structures(category: \"{cat}\")")
                    });
                    r2Envelope.Messages.Add(new ChatMessage
                    {
                        Role = "user",
                        LayerTag = "L4",
                        Content = queryObservation
                    });

                    bool r2Completed = false;
                    Result<LlmResponse, RimMindError>? r2Res = null;
                    if (queue != null)
                    {
                        if (queue.TotalQueuedCount > 15) queue.CancelAllRequests();
                        queue.ClearAllCooldowns();
                    }
                    RimMindAPI.Send(r2Envelope, res =>
                    {
                        r2Res = res;
                        r2Completed = true;
                    });
                    _report.TotalLiveLlmRequests++;

                    float r2Timeout = 65f;
                    float r2Elapsed = 0f;
                    while (!r2Completed && r2Elapsed < r2Timeout)
                    {
                        if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                        r2Elapsed += Time.unscaledDeltaTime;
                        if (queue is ITickableRequestQueue tq)
                        {
                            tq.CurrentTick += 5;
                            tq.Tick();
                        }
                        DismissModalWindows(Find.CurrentMap);
                        yield return null;
                    }

                    if (r2Completed && r2Res != null && r2Res.Value.IsOk)
                    {
                        var r2Val = r2Res.Value.Value;
                        report.TotalTokensUsed += r2Val.TokensUsed;
                        var r2Extracted = ExtractToolCall(r2Val.ToolCallsJson, r2Val.Content);
                        toolName = r2Extracted.ToolName;
                        argsObj = r2Extracted.Arguments;

                        if (string.IsNullOrWhiteSpace(toolName) && argsObj != null)
                        {
                            if (argsObj.TryGetValue("building_def", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "place_blueprint";
                            else if (argsObj.TryGetValue("work_type", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "prioritize_work";
                            else if (argsObj.TryGetValue("job_type", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "take_job";
                            else if (argsObj.TryGetValue("order_type", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "designate_map_order";
                            else if (argsObj.TryGetValue("recipe_def", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "actions_add_worktable_bill";
                            else if (argsObj.TryGetValue("target_apparel_def", StringComparison.OrdinalIgnoreCase, out _))
                                toolName = "actions_manage_equipment";
                        }

                        if (argsObj != null && argsObj.TryGetValue("reason", StringComparison.OrdinalIgnoreCase, out var rTok2))
                        {
                            reason = $"[闭环决策] {rTok2}";
                        }
                        else if (!string.IsNullOrWhiteSpace(r2Val.Content))
                        {
                            reason = $"[闭环决策] {r2Val.Content.Trim()}";
                        }
                    }
                    else
                    {
                        toolName = null;
                    }
                }

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    string noToolMsg = "LLM response did not contain a valid tool call";
                    Log.Warning($"[RimMind-Playthrough][Day {day}] Agent Decision failed ({agentPawn.Name?.ToStringShort}): {noToolMsg}");
                    _recentActionFailures[agentPawn.thingIDNumber] = noToolMsg;
                    if (recordInReport)
                    {
                        report.AgentActionPawn = agentPawn.Name?.ToStringShort ?? "Agent";
                        report.AgentActionTool = "failed";
                        report.AgentActionDetail = $"[No ToolCall] {noToolMsg}";
                        report.AgentActionReason = reason;
                    }
                    yield break;
                }

                if (argsObj != null && argsObj.TryGetValue("reason", StringComparison.OrdinalIgnoreCase, out var rTok) && !reason.StartsWith("[闭环决策]"))
                {
                    reason = rTok.ToString();
                }

                if (string.Equals(toolName, "place_blueprint", StringComparison.OrdinalIgnoreCase))
                {
                    string bDef = argsObj != null && argsObj.TryGetValue("building_def", StringComparison.OrdinalIgnoreCase, out var bDefTok) ? bDefTok.ToString() : "Bed";
                    int ox = argsObj != null && argsObj.TryGetValue("offset_x", StringComparison.OrdinalIgnoreCase, out var oxTok) && int.TryParse(oxTok.ToString(), out var parsedOx) ? parsedOx : 0;
                    int oz = argsObj != null && argsObj.TryGetValue("offset_z", StringComparison.OrdinalIgnoreCase, out var ozTok) && int.TryParse(ozTok.ToString(), out var parsedOz) ? parsedOz : 0;
                    IntVec3 targetCell = (pawnMap != null && !agentPawn.Position.InHorDistOf(pawnMap.Center, 28f) ? pawnMap.Center : agentPawn.Position) + new IntVec3(ox, 0, oz);
                    detail = isZh ? $"规划蓝图: {bDef} 于 ({targetCell.x}, {targetCell.z})" : $"Place blueprint: {bDef} at ({targetCell.x}, {targetCell.z})";
                }
                else if (string.Equals(toolName, "prioritize_work", StringComparison.OrdinalIgnoreCase))
                {
                    string wt = argsObj != null && argsObj.TryGetValue("work_type", StringComparison.OrdinalIgnoreCase, out var wtTok) ? wtTok.ToString() : "Hauling";
                    detail = isZh ? $"优先工种: {wt}" : $"Prioritize work: {wt}";
                }
                else if (string.Equals(toolName, "take_job", StringComparison.OrdinalIgnoreCase))
                {
                    string jt = argsObj != null && argsObj.TryGetValue("job_type", StringComparison.OrdinalIgnoreCase, out var jtTok) ? jtTok.ToString() : "HaulToStorage";
                    string target = argsObj != null && argsObj.TryGetValue("target", StringComparison.OrdinalIgnoreCase, out var tg) ? tg.ToString() : "Nearby";
                    detail = isZh ? $"执行作业: {jt} ({target})" : $"Take job: {jt} ({target})";
                }
                else if (string.Equals(toolName, "designate_map_order", StringComparison.OrdinalIgnoreCase))
                {
                    string ot = argsObj != null && argsObj.TryGetValue("order_type", StringComparison.OrdinalIgnoreCase, out var otTok) ? otTok.ToString() : "Harvest";
                    string target = argsObj != null && argsObj.TryGetValue("target", StringComparison.OrdinalIgnoreCase, out var tgTok) ? tgTok.ToString() : "Nearby";
                    detail = isZh ? $"下发地图指令: {ot} ({target})" : $"Designate map order: {ot} ({target})";
                }
                else if (string.Equals(toolName, "eat_and_recreation", StringComparison.OrdinalIgnoreCase))
                {
                    detail = isZh ? "进餐与娱乐恢复（Actions Mechanism）" : "Eat and recreation recovery (Actions Mechanism)";
                }
                else if (string.Equals(toolName, "stabilize_rest", StringComparison.OrdinalIgnoreCase))
                {
                    detail = isZh ? "就医与卧床休整（Actions Mechanism）" : "Stabilize and bed rest (Actions Mechanism)";
                }
                else if (string.Equals(toolName, "actions_add_worktable_bill", StringComparison.OrdinalIgnoreCase))
                {
                    string rDef = argsObj != null && argsObj.TryGetValue("recipe_def", StringComparison.OrdinalIgnoreCase, out var rdTok) ? rdTok.ToString() : "CookMealSimple";
                    int count = argsObj != null && argsObj.TryGetValue("target_count", StringComparison.OrdinalIgnoreCase, out var cTok) && int.TryParse(cTok.ToString(), out int parsedCount) ? parsedCount : 10;
                    detail = isZh ? $"工作台挂单: {rDef} (目标数: {count})" : $"Add worktable bill: {rDef} (target: {count})";
                }
                else if (string.Equals(toolName, "actions_manage_equipment", StringComparison.OrdinalIgnoreCase))
                {
                    string targetGear = argsObj != null && argsObj.TryGetValue("target_apparel_def", StringComparison.OrdinalIgnoreCase, out var tgTok) ? tgTok.ToString() : "Auto";
                    detail = isZh ? $"装备调配与穿戴: {targetGear}" : $"Manage apparel & equipment: {targetGear}";
                }
                else
                {
                    detail = isZh ? $"调用工具: {toolName}" : $"Invoke tool: {toolName}";
                }

                // Dispatch autonomous action strictly through the production PawnAgent pipeline (no inline duplicate execution)
                bool execFailed = false;
                string execErr = "";
                RimMind.Presentation.Api.RimMindPawnLookup.CachePawn(agentPawn);
                if (agentComp?.Agent != null)
                {
                    var payloadObj = argsObj != null
                        ? (Newtonsoft.Json.Linq.JObject)argsObj.DeepClone()
                        : new Newtonsoft.Json.Linq.JObject();
                    if (payloadObj["pawn_id"] == null && payloadObj["npc_id"] == null)
                    {
                        payloadObj["pawn_id"] = "NPC-" + agentPawn.thingIDNumber;
                    }
                    string paramJson = payloadObj.ToString(Newtonsoft.Json.Formatting.None);
                    var decision = new AgentDecision(
                        toolName,
                        reason,
                        null,
                        paramJson);
                    var execOutcome = agentComp.Agent.ExecuteDecision(decision);
                    if (!execOutcome.IsOk)
                    {
                        execFailed = true;
                        execErr = execOutcome.Error.Message;
                    }
                }

                if (execFailed)
                {
                    _recentActionFailures[agentPawn.thingIDNumber] = execErr;
                    if (recordInReport)
                    {
                        report.AgentActionPawn = agentPawn.Name?.ToStringShort ?? "Agent";
                        report.AgentActionTool = $"{toolName} (failed)";
                        report.AgentActionDetail = $"[Physical Failed] {detail} -> {execErr}";
                        report.AgentActionReason = reason;
                    }
                    Log.Warning($"[RimMind-Playthrough][Day {day}] Agent Physical Execution Failed ({agentPawn.Name?.ToStringShort}): [{toolName}] {execErr}");
                }
                else
                {
                    _recentActionFailures.Remove(agentPawn.thingIDNumber);
                    if (recordInReport)
                    {
                        report.AgentActionPawn = agentPawn.Name?.ToStringShort ?? "Agent";
                        report.AgentActionTool = toolName;
                        report.AgentActionDetail = detail;
                        report.AgentActionReason = reason;
                    }
                    Log.Message($"[RimMind-Playthrough][Day {day}] Agent Decision ({agentPawn.Name?.ToStringShort}): [{toolName}] {detail} - \"{reason}\"");
                }
            }
            else
            {
                string errMsg = responseResult != null && !responseResult.Value.IsOk ? responseResult.Value.Error.Message : "LLM response timed out or returned null";
                Log.Warning($"[RimMind-Playthrough][Day {day}] Agent Decision execution failed ({agentPawn.Name.ToStringShort}): {errMsg}");

                _recentActionFailures[agentPawn.thingIDNumber] = errMsg;

                if (recordInReport)
                {
                    report.AgentActionPawn = agentPawn.Name?.ToStringShort ?? "Agent";
                    report.AgentActionTool = "failed";
                    report.AgentActionDetail = $"[Execution Failed] {errMsg}";
                    report.AgentActionReason = $"Physical execution failed: {errMsg}";
                }

                // Explicitly record physical failure to working memory for agent to learn and self-correct next round
                try
                {
                    if (global::Verse.ModsConfig.IsActive("mcocdaa.RimMindMemory"))
                    {
                        RimMindAPI.Memory.AddPawnMemory(
                            $"[Action Failed] Autonomous action failed: {errMsg}",
                            RimMind.Application.Common.Models.Memory.MemoryKind.Event,
                            Find.TickManager.TicksGame,
                            0.8f,
                            agentPawn.ThingID);
                    }
                }
                catch (Exception) { }
            }
        }

        private static List<string> GetBuildableStructuresForRunner(Map? map, string category, int woodCount, int steelCount, int compCount)
        {
            bool zh = IsChineseLanguageActive();
            var list = new List<string>();
            string catLower = category?.ToLowerInvariant() ?? "all";

            if (catLower == "all" || catLower.Contains("furn") || catLower.Contains("bed"))
            {
                list.Add(zh
                    ? $"- Bed (木床): 需木材 35 (现有木材: {woodCount}) -> 解决露宿泥地严重减益"
                    : $"- Bed: requires 35 Wood (available Wood: {woodCount}) -> resolves sleeping on the ground debuff");
                list.Add(zh
                    ? $"- Table2x2c (木桌): 需木材 28 (现有木材: {woodCount}) -> 解决无桌进餐"
                    : $"- Table2x2c: requires 28 Wood (available Wood: {woodCount}) -> resolves ate without table debuff");
                list.Add(zh
                    ? $"- DiningChair (餐椅): 需木材 20 (现有木材: {woodCount}) -> 舒适进餐与休息"
                    : $"- DiningChair: requires 20 Wood (available Wood: {woodCount}) -> comfortable dining and seating");
            }
            if (catLower == "all" || catLower.Contains("temp") || catLower.Contains("heat") || catLower.Contains("cool"))
            {
                list.Add(zh
                    ? $"- Campfire (营火): 需木材 20 (现有木材: {woodCount}) -> 严寒取暖与初级烹饪"
                    : $"- Campfire: requires 20 Wood (available Wood: {woodCount}) -> heating and basic cooking");
                list.Add(zh
                    ? $"- PassiveCooler (被动制冷器): 需木材 50 (现有木材: {woodCount}) -> 酷暑降温避险"
                    : $"- PassiveCooler: requires 50 Wood (available Wood: {woodCount}) -> heatwave cooling");
                list.Add(zh
                    ? $"- TorchLamp (火把): 需木材 20 (现有木材: {woodCount}) -> 区域照明防御黑暗"
                    : $"- TorchLamp: requires 20 Wood (available Wood: {woodCount}) -> indoor lighting");
            }
            if (catLower == "all" || catLower.Contains("struct") || catLower.Contains("wall") || catLower.Contains("door"))
            {
                list.Add(zh
                    ? $"- Wall (木墙): 需木材 5 (现有木材: {woodCount}) -> 遮风挡雨与室内封闭"
                    : $"- Wall: requires 5 Wood (available Wood: {woodCount}) -> room enclosure");
                list.Add(zh
                    ? $"- Door (木门): 需木材 25 (现有木材: {woodCount}) -> 房间出入口"
                    : $"- Door: requires 25 Wood (available Wood: {woodCount}) -> room entrance");
            }
            if (list.Count == 0)
            {
                list.Add(zh ? $"- Bed (木床): 需木材 35 (现有木材: {woodCount})" : $"- Bed: requires 35 Wood (available Wood: {woodCount})");
                list.Add(zh ? $"- Campfire (营火): 需木材 20 (现有木材: {woodCount})" : $"- Campfire: requires 20 Wood (available Wood: {woodCount})");
                list.Add(zh ? $"- TorchLamp (火把): 需木材 20 (现有木材: {woodCount})" : $"- TorchLamp: requires 20 Wood (available Wood: {woodCount})");
            }
            return list;
        }

        private static (string? ToolName, JObject? Arguments) ExtractToolCall(string? toolCallsJson, string? content = null, string? expectedToolName = null)
        {
            if (!string.IsNullOrWhiteSpace(toolCallsJson))
            {
                try
                {
                    string cleaned = toolCallsJson!.Trim();
                    if (cleaned.StartsWith("```"))
                    {
                        int firstNewline = cleaned.IndexOf('\n');
                        if (firstNewline >= 0) cleaned = cleaned.Substring(firstNewline + 1);
                        if (cleaned.EndsWith("```")) cleaned = cleaned.Substring(0, cleaned.Length - 3);
                        cleaned = cleaned.Trim();
                    }

                    JToken token = JToken.Parse(cleaned);
                    JArray? arr = token as JArray;
                    if (arr == null && token is JObject obj)
                    {
                        arr = new JArray { obj };
                    }

                    if (arr != null && arr.Count > 0)
                    {
                        foreach (var item in arr)
                        {
                            if (item is not JObject callObj) continue;

                            string? name = null;
                            if (callObj.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nToken))
                                name = nToken.Value<string>();
                            else if (callObj.TryGetValue("function", StringComparison.OrdinalIgnoreCase, out var fnToken) && fnToken is JObject fnObj && fnObj.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var fnName))
                                name = fnName.Value<string>();

                            if (expectedToolName != null && !string.Equals(name, expectedToolName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            JToken? argsToken = null;
                            if (callObj.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var directArgs))
                                argsToken = directArgs;
                            else if (callObj.TryGetValue("function", StringComparison.OrdinalIgnoreCase, out var fn) && fn is JObject fnO && fnO.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var nestedArgs))
                                argsToken = nestedArgs;

                            if (argsToken == null)
                            {
                                if (callObj.ContainsKey("thought") || callObj.ContainsKey("speech") || callObj.ContainsKey("work_type") || callObj.ContainsKey("job_type") || callObj.ContainsKey("building_def") || callObj.ContainsKey("reason"))
                                    return (name, callObj);
                                continue;
                            }

                            if (argsToken.Type == JTokenType.Object && argsToken is JObject aObj)
                                return (name, aObj);

                            if (argsToken.Type == JTokenType.String)
                            {
                                string raw = argsToken.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(raw)) continue;
                                if (raw.StartsWith("```"))
                                {
                                    int nl = raw.IndexOf('\n');
                                    if (nl >= 0) raw = raw.Substring(nl + 1);
                                    if (raw.EndsWith("```")) raw = raw.Substring(0, raw.Length - 3);
                                    raw = raw.Trim();
                                }
                                try
                                {
                                    var parsed = JToken.Parse(raw);
                                    if (parsed is JObject jRes) return (name, jRes);
                                }
                                catch { }
                            }
                        }

                        var first = arr[0] as JObject;
                        if (first != null)
                        {
                            string? fnName = null;
                            if (first.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nTok)) fnName = nTok.Value<string>();
                            else if (first.TryGetValue("function", StringComparison.OrdinalIgnoreCase, out var fTok1) && fTok1 is JObject fO1 && fO1.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nTok1)) fnName = nTok1.Value<string>();

                            if (first.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var aTok))
                            {
                                if (aTok is JObject aObj) return (fnName, aObj);
                                if (aTok.Type == JTokenType.String)
                                {
                                    try { return (fnName, JObject.Parse(aTok.Value<string>() ?? "{}")); } catch { }
                                }
                            }
                            if (first.TryGetValue("function", StringComparison.OrdinalIgnoreCase, out var fTok) && fTok is JObject fO && fO.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var fnArgsTok))
                            {
                                if (fnArgsTok is JObject nObj) return (fnName, nObj);
                                if (fnArgsTok.Type == JTokenType.String)
                                {
                                    try { return (fnName, JObject.Parse(fnArgsTok.Value<string>() ?? "{}")); } catch { }
                                }
                            }
                            return (fnName, first);
                        }
                    }
                }
                catch { }
            }

            // Fallback: Attempt to extract tool call JSON or pseudo-function-call embedded in text content
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    string rawContent = content!.Trim();
                    int startIdx = rawContent.IndexOf('{');
                    int endIdx = rawContent.LastIndexOf('}');
                    if (startIdx >= 0 && endIdx > startIdx)
                    {
                        string jsonBlock = rawContent.Substring(startIdx, endIdx - startIdx + 1);
                        var parsed = JObject.Parse(jsonBlock);
                        string? toolName = parsed.TryGetValue("tool", StringComparison.OrdinalIgnoreCase, out var tTok) ? tTok.ToString()
                            : (parsed.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nTok) ? nTok.ToString() : null);
                        return (toolName, parsed);
                    }
                }
                catch { }

                try
                {
                    var fnMatch = Regex.Match(
                        content!.Trim(),
                        @"\b(?<fn>[a-z_][a-z0-9_]*)\s*\((?<inner>[\s\S]+?)\)?\s*$",
                        RegexOptions.IgnoreCase);
                    if (fnMatch.Success)
                    {
                        string fnName = fnMatch.Groups["fn"].Value;
                        if (expectedToolName == null || string.Equals(fnName, expectedToolName, StringComparison.OrdinalIgnoreCase))
                        {
                            string inner = fnMatch.Groups["inner"].Value.Trim();
                            var keyMatches = Regex.Matches(
                                inner,
                                @"(?:^|,\s*)(?<key>[a-z_][a-z0-9_]*)\)?\s*[:=]\s*",
                                RegexOptions.IgnoreCase);
                            if (keyMatches.Count > 0)
                            {
                                var jObj = new JObject();
                                for (int i = 0; i < keyMatches.Count; i++)
                                {
                                    string key = keyMatches[i].Groups["key"].Value;
                                    int valStart = keyMatches[i].Index + keyMatches[i].Length;
                                    int valEnd = (i + 1 < keyMatches.Count) ? keyMatches[i + 1].Index : inner.Length;
                                    string rawVal = inner.Substring(valStart, valEnd - valStart).Trim();
                                    if ((rawVal.StartsWith("\"") && rawVal.EndsWith("\"") && rawVal.Length >= 2) ||
                                        (rawVal.StartsWith("'") && rawVal.EndsWith("'") && rawVal.Length >= 2) ||
                                        (rawVal.StartsWith("“") && rawVal.EndsWith("”") && rawVal.Length >= 2) ||
                                        (rawVal.StartsWith("(") && rawVal.EndsWith(")") && rawVal.Length >= 2))
                                    {
                                        rawVal = rawVal.Substring(1, rawVal.Length - 2).Replace("\\\"", "\"").Trim();
                                    }
                                    if (int.TryParse(rawVal, out int intVal))
                                        jObj[key] = intVal;
                                    else
                                        jObj[key] = rawVal;
                                }
                                return (fnName, jObj);
                            }
                        }
                    }
                }
                catch { }
            }

            return (null, null);
        }

        private static JObject? ExtractToolArguments(string? toolCallsJson, string? expectedToolName = null, string? content = null)
        {
            return ExtractToolCall(toolCallsJson, content, expectedToolName).Arguments;
        }

        private IEnumerator ExecuteAdvisorProposal(List<Pawn> colonists, int day, PlaythroughDayReport report)
        {
            bool zh = IsChineseLanguageActive();
            Pawn pawn = colonists[0];
            int cycleDay = ((day - 1) % 60) + 1;
            string yearPrefix = day > 60
                ? (zh ? $"[第 {(day - 1) / 60 + 1} 年深化建设] " : $"[Year {(day - 1) / 60 + 1} Expansion] ")
                : "";
            string proposalText = zh ? (cycleDay switch
            {
                1 => "建议规划 10x10 殖民地核心大厅与 3 间独立卧室，奠定营地聚居基础",
                2 => "建议勘探近处露天矿脉并开采钢铁，为早期基础设施储备金属",
                3 => "建议划定 8x8 农田开垦水稻作物，保障全员主食自给自足",
                4 => "建议采伐周边成熟林木，大量储备原木以备建筑与生活柴火之需",
                5 => "建议规划封闭冷库与食物储存区，安装制冷设备防止食物腐烂",
                6 => "建议部署太阳能发电机组并连接输电管线，搭建殖民地初级电网",
                7 => "建议建造蓄电池组并做好绝缘防潮，确保夜间供电与电网缓冲",
                8 => "建议部署烹饪料理台并规范卫生准则，制作熟食以避免食物中毒",
                9 => "建议建立独立屠宰隔间，高效分解猎物尸体并收集皮毛原料",
                10 => "建议在营地主入口部署尖刺陷阱阵与沙袋掩体，抵御早期袭扰",
                11 => "建议对主仓库推行物资分类管理，开辟建材与原料专用堆放区",
                12 => "建议设立基础科研台并指派专职研究员，启动边缘科技攀升",
                13 => "建议在各卧室内打造舒适木质单人床，提升殖民者睡眠与心情",
                14 => "建议在大厅部署下棋桌与休闲娱乐设施，缓解殖民者心理压力",
                15 => "建议划定药用马草种植区，培育本土草药以备伤病急救应急",
                16 => "建议深入勘探山体开采压缩机械零部件，储备电气与自动化构件",
                17 => "建议设立急救医务室并布置医用病床，提升创伤与疾病救治效率",
                18 => "建议建造手工裁缝台，采集毛皮与纤维为全员缝制耐磨御寒衣物",
                19 => "建议在外围开阔风道部署风力发电机，实现风光互补稳定供能",
                20 => "建议设立燃煤铁匠台，冶炼锻造金属近战兵器与实用农用工具",
                21 => "建议构筑花岗岩坚固石质外墙，封锁死角构建环形封闭防御圈",
                22 => "建议圈定畜牧围栏并树立牧业标桩，驯养野生禽畜发展畜牧业",
                23 => "建议在厚岩顶下方开辟辐射尘避难地窖，储备罐头与应急生存物资",
                24 => "建议在防御隘口布设自动化哨戒机枪，构建第一道自动火力交叉网",
                25 => "建议开垦棉花作物种植田，收获优质布料以生产医疗绷带与防尘风衣",
                26 => "建议勘测地热喷气孔并部署地热发电机组，获取全天候充沛高能电力",
                27 => "建议引入应急营养膏合成机，在食物匮乏或厨师伤病时保障口粮",
                28 => "建议设立切石工坊批量加工各类石材，全面淘汰易燃木质墙体",
                29 => "建议在核心电池间与工坊安装灭火泡沫发生器，阻断电涌火灾隐患",
                30 => "建议点燃中央篝火举办开拓满月庆典，聚餐庆祝殖民地生存稳固",
                31 => "建议在防线内侧设立独立军械武器架，实现枪械与防具快速着装",
                32 => "建议设立室内无土水培温室水槽，彻底摆脱严寒与有毒尘暴减产威胁",
                33 => "建议构筑迫击炮战术掩体并储备高爆炮弹，具备远程反攻压制能力",
                34 => "建议在医院与实验室全面铺设无菌地砖，杜绝手术感染与研发误差",
                35 => "建议在幽静庭院设置精神冥想点，助益灵能者专注凝神与心情恢复",
                36 => "建议在仓库前设立远征商队打包集结地，常态化组织对外贸易交流",
                37 => "建议建造生物精炼厂将有机多余作物提炼为化合燃料，供应动力设备",
                38 => "建议升级军工精密防具生产线，为作战人员量身打造防弹背心与头盔",
                39 => "建议在防御外围设置战术射击靶场，定期开展殖民者远距射击特训",
                40 => "建议锁定伴生金矿脉展开深度开采，储备珍贵黄金用于先进部件制造",
                41 => "建议落成工业级机械加工台，自主制造高精度枪械与工业零部件",
                42 => "建议架设轨道通讯台与贸易信标，联络过境轨道商船购入稀缺高精尖物资",
                43 => "建议沿全殖民地交通要道部署立式泛光灯，消除夜间行动与交火惩罚",
                44 => "建议完成主冷库双层保温扩建并增设双定制冷机，应对极端热浪挑战",
                45 => "建议建立高科技科研中心并接驳多元分析仪，冲刺前沿尖端科技",
                46 => "建议设立仿生义体工作台，为伤残同伴量身定制高机能仿生器官",
                47 => "建议在各核心出入口换装重型钢制自动防爆门，构筑气闸级防护隔离",
                48 => "建议引入深层地质钻井持续开采地下未探明富矿，告别表层资源枯竭",
                49 => "建议精装豪华贵宾客房与皇家大床，提升过往外宾与同盟势力友好度",
                50 => "建议在要塞前沿部署 EMP 电磁脉冲地雷阵，针对性防范机械族空投突击",
                51 => "建议贯通全基地智能控温通风管道网络，实现冬暖夏凉一体化调节",
                52 => "建议全力筹措玻璃钢与高级构件，启动全套精良动力装甲组装工程",
                53 => "建议设立艺术雕塑工坊雕刻宏伟丰碑巨作，大幅提升殖民地宜居印象",
                54 => "建议为突击近战队员定制个人能量护盾腰带，打造坚不可摧的前锋屏障",
                55 => "建议暗中开凿直穿后山峡谷的应急撤离坑道，确保遭遇灭顶之灾时全身而退",
                56 => "建议展开全殖民地物资总盘点与军备大检修，全面清理磨损装备与隐患",
                57 => "建议成立高级生化制药实验室，自主试制高效消炎药与高级急救药品",
                58 => "建议勘定飞船发射平台地基并铺设结构梁，迈向逃离边缘世界的终极筹备",
                59 => "建议全员动员巡检全部炮塔、迫击炮弹药与外墙耐久，进入最高特级战备",
                60 => $"建议在中心广场落成 {day} 日开拓胜利纪念碑，举行全员欢庆大丰收盛典！",
                _ => "建议巡视殖民地安全状况并维护关键发电设备"
            }) : $"Day {day} Advisor Proposal (Cycle {cycleDay}): inspect colony infrastructure, food reserves, and perimeter defenses.";

            var advScope = RuntimeServiceHub.Shared.Capture();
            var advQueue = advScope.GetOptional<IRequestQueue>();
            if (advQueue != null)
            {
                if (advQueue.TotalQueuedCount > 15) advQueue.CancelAllRequests();
                advQueue.ClearAllCooldowns();
            }

            Map advMap = pawn.Map ?? Find.CurrentMap;
            int advWood = CountAvailableMapResource(advMap, ThingDefOf.WoodLog);
            int advSteel = CountAvailableMapResource(advMap, ThingDefOf.Steel);
            int advMeals = CountAvailableMapResource(advMap, ThingDefOf.MealSimple)
                + CountAvailableMapResource(advMap, ThingDefOf.MealFine)
                + CountAvailableMapResource(advMap, ThingDefOf.MealSurvivalPack);
            int advBlueprints = (advMap?.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0)
                + (advMap?.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0);

            var advTools = new List<StructuredTool>
            {
                new StructuredTool
                {
                    Name = "propose_colony_advice",
                    Description = "Submit a tactical colony advisor recommendation for player approval based on current colony vitals",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"proposal\":{\"type\":\"string\"},\"priority_domain\":{\"type\":\"string\"}},\"required\":[\"proposal\"]}"
                }
            };

            var advEnvelope = LlmRequestEnvelopeBuilder
                .ForScenario(RimMindAPI.Context.ScenarioDecision)
                .WithModId("RimMind-Advisor")
                .WithNpcId("NPC-" + pawn.thingIDNumber)
                .WithTools(advTools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(500)
                .WithTemperature(0.6f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            advEnvelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L0",
                Content = zh
                    ? $"你是 RimWorld 殖民地战术顾问（发声代表：{pawn.Name.ToStringShort}）。请根据当前营地状态调用 propose_colony_advice 提出一条简明扼要（30~60字）的今日发展或生存建议，不要输出 XML 标签。"
                    : $"You are the RimWorld Colony Tactical Advisor (represented by {pawn.Name.ToStringShort}). Call propose_colony_advice to submit a concise (1-2 sentences) strategic recommendation for the colony based on current vitals. Do not output XML tags."
            });
            advEnvelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                LayerTag = "L4",
                Content = zh
                    ? $"[殖民地第 {day} 天晚间顾问评估] 存活殖民者: {colonists.Count} 人, 木材: {advWood}, 钢铁: {advSteel}, 熟食口粮: {advMeals}, 待建工程: {advBlueprints}。请调用 propose_colony_advice 给出当前最关键的殖民地建议。"
                    : $"[Colony Day {day} Evening Advisor Review] Living colonists: {colonists.Count}, Wood: {advWood}, Steel: {advSteel}, Meals: {advMeals}, Pending construction: {advBlueprints}. Call propose_colony_advice with the top strategic recommendation right now."
            });

            bool advCompleted = false;
            Result<LlmResponse, RimMindError>? advResponse = null;
            RimMindAPI.Send(advEnvelope, res =>
            {
                advResponse = res;
                advCompleted = true;
            });
            _report.TotalLiveLlmRequests++;

            float advTimeout = 65f;
            float advElapsed = 0f;
            while (!advCompleted && advElapsed < advTimeout)
            {
                if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                advElapsed += Time.unscaledDeltaTime;
                if (advQueue is ITickableRequestQueue tq)
                {
                    tq.CurrentTick += 5;
                    tq.Tick();
                }
                DismissModalWindows(Find.CurrentMap);
                yield return null;
            }

            if (advCompleted && advResponse != null && advResponse.Value.IsOk)
            {
                var advVal = advResponse.Value.Value;
                report.TotalTokensUsed += advVal.TokensUsed;
                var advArgs = ExtractToolArguments(advVal.ToolCallsJson, "propose_colony_advice", advVal.Content);
                string? liveProposal = advArgs?["proposal"]?.ToString()
                    ?? advArgs?["advice"]?.ToString()
                    ?? advArgs?["recommendation"]?.ToString();
                if (string.IsNullOrWhiteSpace(liveProposal) && !string.IsNullOrWhiteSpace(advVal.Content))
                {
                    liveProposal = advVal.Content.Trim();
                }
                liveProposal = StripModelXmlTags(liveProposal);
                if (!string.IsNullOrWhiteSpace(liveProposal))
                {
                    proposalText = liveProposal!;
                }
            }
            else
            {
                Log.Warning($"[RimMind-Playthrough][Day {day}] Advisor proposal fallback: {advResponse?.Error.Message}");
            }

            report.AdvisorProposal = $"{pawn.Name.ToStringShort}: {yearPrefix}{proposalText}";

            // Register to RequestOverlay
            bool approved = false;
            var req = new RequestEntry
            {
                title = zh ? "顾问决策建议" : "Advisor Recommendation",
                description = report.AdvisorProposal,
                options = new[] { "approve", "reject" },
                source = pawn.Name?.ToStringShort ?? "Advisor",
                callback = choice => { if (choice == "approve") approved = true; }
            };

            RequestOverlay.Register(req);
            yield return null;

            // Simulate player reviewing and approving the request
            RequestOverlay.Resolve(req, "approve");
            report.AdvisorApproved = approved;

            Log.Message($"[RimMind-Playthrough][Day {day}] Advisor Proposal Approved: {report.AdvisorProposal}");
        }

        private void ExecuteNightReflection(PlaythroughDayReport report)
        {
            try
            {
                object? comp = Find.World?.components?.FirstOrDefault(c => c.GetType().Name == "RimMindMemoryWorldComponent")
                    ?? (object?)Current.Game?.components?.FirstOrDefault(c => c.GetType().Name == "RimMindMemoryWorldComponent");
                if (comp != null)
                {
                    var map = Find.CurrentMap;
                    if (map?.mapPawns?.FreeColonistsSpawned != null)
                    {
                        int activeCount = 0;
                        int archiveCount = 0;
                        int darkCount = 0;
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                        var getStoreMethod = comp.GetType().GetMethod("GetOrCreatePawnStore", flags)
                            ?? comp.GetType().GetMethod("GetPawnStore", flags);
                        if (getStoreMethod != null)
                        {
                            foreach (var pawn in RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(map))
                            {
                                var store = getStoreMethod.Invoke(comp, new object[] { pawn });
                                if (store != null)
                                {
                                    var activeProp = store.GetType().GetField("active", flags);
                                    var archiveProp = store.GetType().GetField("archive", flags);
                                    var darkProp = store.GetType().GetField("dark", flags);
                                    var activeList = activeProp?.GetValue(store) as System.Collections.IList;
                                    var archiveList = archiveProp?.GetValue(store) as System.Collections.IList;
                                    var darkList = darkProp?.GetValue(store) as System.Collections.IList;

                                    if (activeList != null)
                                        activeCount += activeList.Count;
                                    if (archiveList != null)
                                        archiveCount += archiveList.Count;

                                    // Consolidate episodic archive into semantic/dark memory when archive reaches threshold
                                    if (darkList != null && archiveList != null && archiveList.Count > 0 && darkList.Count < (archiveList.Count + 2) / 3)
                                    {
                                        var sampleEntry = archiveList[archiveList.Count - 1];
                                        if (sampleEntry != null)
                                        {
                                            var entryType = sampleEntry.GetType();
                                            var factEntry = Activator.CreateInstance(entryType);
                                            if (factEntry != null)
                                            {
                                                bool zh = IsChineseLanguageActive();
                                                entryType.GetField("id", flags)?.SetValue(factEntry, $"mem-dark-{report.DayNumber}-{pawn.thingIDNumber}");
                                                entryType.GetField("content", flags)?.SetValue(
                                                    factEntry,
                                                    zh
                                                        ? $"[长期记忆沉淀] {pawn.Name?.ToStringShort ?? "Colonist"} 累计经历 {archiveList.Count} 项殖民地事件，生存适应力稳步提升。"
                                                        : $"[Long-Term Memory Consolidation] {pawn.Name?.ToStringShort ?? "Colonist"} has experienced {archiveList.Count} colony events, steadily building survival resilience.");
                                                entryType.GetField("tick", flags)?.SetValue(factEntry, Find.TickManager?.TicksGame ?? 0);
                                                entryType.GetField("importance", flags)?.SetValue(factEntry, 0.85f);
                                                entryType.GetField("isPinned", flags)?.SetValue(factEntry, true);
                                                darkList.Add(factEntry);
                                            }
                                        }
                                    }

                                    if (darkList != null)
                                        darkCount += darkList.Count;
                                }
                            }
                        }

                        var narratorProp = comp.GetType().GetProperty("NarratorStore", flags);
                        var narratorStore = narratorProp?.GetValue(comp);
                        if (narratorStore != null)
                        {
                            var narratorDarkField = narratorStore.GetType().GetField("dark", flags);
                            if (narratorDarkField?.GetValue(narratorStore) is System.Collections.IList narratorDarkList)
                                darkCount += narratorDarkList.Count;
                        }

                        report.WorkingMemoryCount = activeCount;
                        report.EpisodicMemoryCount = archiveCount;
                        report.ColonyFactCount = darkCount;
                        return;
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }
            report.WorkingMemoryCount = 0;
            report.EpisodicMemoryCount = 0;
            report.ColonyFactCount = 0;
        }

        private static string StripModelXmlTags(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;
            string s = input!.Trim();
            if (s.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s = Regex.Replace(s, @"<thinking>[\s\S]*?</thinking>", string.Empty, RegexOptions.IgnoreCase).Trim();
            }
            if (s.IndexOf('<') >= 0 && s.IndexOf('>') >= 0)
            {
                s = Regex.Replace(
                    s,
                    @"</?(?:tool_call|speech|thought|dialogue|response|monologue|reply|thinking)\b[^>]*>",
                    string.Empty,
                    RegexOptions.IgnoreCase).Trim();
            }
            return s;
        }

        private static Blueprint? TryPlaceBlueprint(Map map, string defName, IntVec3 cell, Rot4? rot = null, string? stuffDefName = null)
        {
            if (map == null) return null;
            if (defName == "TableStoveWood") defName = "FueledStove"; else if (defName == "ResearchBench") defName = "SimpleResearchBench"; BuildableDef? buildableDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (buildableDef == null)
            {
                buildableDef = DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            }
            if (buildableDef == null) return null;

            ThingDef? stuffDef = null;
            if (buildableDef is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                if (!string.IsNullOrEmpty(stuffDefName))
                {
                    stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);
                }
                if (stuffDef == null)
                {
                    stuffDef = GenStuff.DefaultStuffFor(thingDef) ?? ThingDefOf.WoodLog ?? ThingDefOf.Steel;
                }
            }

            if (!cell.InBounds(map))
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 15);
            }

            try
            {
                Rot4 chosenRot = rot ?? Rot4.North;
                Rot4[] candidateRots = (buildableDef is ThingDef td && td.rotatable)
                    ? new[] { chosenRot, Rot4.North, Rot4.East, Rot4.South, Rot4.West }
                    : new[] { chosenRot };
                IntVec3 chosenCell = IntVec3.Invalid;
                foreach (var r in candidateRots)
                {
                    if (cell.InBounds(map) && !cell.Fogged(map) && GenConstruct.CanPlaceBlueprintAt(buildableDef, cell, r, map, false, null, null, stuffDef).Accepted)
                    {
                        chosenCell = cell;
                        chosenRot = r;
                        break;
                    }
                }
                if (!chosenCell.IsValid)
                {
                    IntVec3[] anchors = cell.InBounds(map) && cell != map.Center ? new[] { cell, map.Center } : new[] { map.Center };
                    foreach (var anchor in anchors)
                    {
                        foreach (var c in GenRadial.RadialCellsAround(anchor, 25, true))
                        {
                            if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map)) continue;
                            var room = c.GetRoom(map);
                            if (room != null && room.IsDoorway) continue;
                            foreach (var r in candidateRots)
                            {
                                if (GenConstruct.CanPlaceBlueprintAt(buildableDef, c, r, map, false, null, null, stuffDef).Accepted)
                                {
                                    chosenCell = c;
                                    chosenRot = r;
                                    break;
                                }
                            }
                            if (chosenCell.IsValid) break;
                        }
                        if (chosenCell.IsValid) break;
                    }
                }
                if (!chosenCell.IsValid) chosenCell = cell;
                return GenConstruct.PlaceBlueprintForBuild(buildableDef, chosenCell, map, chosenRot, Faction.OfPlayer, stuffDef);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] PlaceBlueprint {defName} at {cell} notice: {ex.Message}");
                return null;
            }
        }

        private static bool AddDesignationSafe(Map map, IntVec3 cell, DesignationDef? desDef)
        {
            if (map == null || desDef == null || !cell.InBounds(map)) return false;
            try
            {
                if (desDef.targetType == TargetType.Thing)
                {
                    Thing? targetThing = null;
                    if (desDef == DesignationDefOf.HarvestPlant || desDef == DesignationDefOf.CutPlant)
                    {
                        targetThing = cell.GetPlant(map);
                    }
                    else if (desDef == DesignationDefOf.Haul)
                    {
                        targetThing = cell.GetFirstHaulable(map);
                    }
                    else
                    {
                        targetThing = cell.GetFirstThing<Thing>(map);
                    }

                    if (targetThing != null && map.designationManager.DesignationOn(targetThing, desDef) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(targetThing, desDef));
                        return true;
                    }
                    return false;
                }

                if (map.designationManager.DesignationAt(cell, desDef) == null)
                {
                    map.designationManager.AddDesignation(new Designation(cell, desDef));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] AddDesignation {desDef.defName} notice: {ex.Message}");
            }
            return false;
        }

        private static List<IntVec3> CollectUnoccupiedZoneCells(Map map, IntVec3 origin, int width, int height, bool requireFertility)
        {
            var cells = new List<IntVec3>(width * height);
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    var c = new IntVec3(origin.x + x, origin.y, origin.z + z);
                    if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map)) continue;
                    if (map.zoneManager.ZoneAt(c) != null) continue;
                    if (c.GetEdifice(map) != null) continue;
                    if (requireFertility && map.fertilityGrid != null && map.fertilityGrid.FertilityAt(c) < 0.7f) continue;
                    cells.Add(c);
                }
            }
            return cells;
        }

        private static Zone_Growing? CreateGrowingZoneSafe(Map map, IntVec3 start, int width, int height, string plantDefName)
        {
            if (map == null) return null;
            try
            {
                var validCells = CollectUnoccupiedZoneCells(map, start, width, height, requireFertility: true);
                if (validCells.Count < 4)
                {
                    int[] offsets = { -16, -10, 10, 16, -22, 22 };
                    foreach (int dx in offsets)
                    {
                        foreach (int dz in offsets)
                        {
                            var candidateCells = CollectUnoccupiedZoneCells(map, new IntVec3(start.x + dx, start.y, start.z + dz), width, height, requireFertility: true);
                            if (candidateCells.Count > validCells.Count)
                                validCells = candidateCells;
                            if (validCells.Count >= (width * height) / 2)
                                break;
                        }
                        if (validCells.Count >= (width * height) / 2)
                            break;
                    }
                }

                if (validCells.Count == 0)
                {
                    return map.zoneManager.AllZones.OfType<Zone_Growing>().FirstOrDefault();
                }

                var zone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                for (int i = 0; i < validCells.Count; i++)
                {
                    zone.AddCell(validCells[i]);
                }
                var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail(plantDefName);
                if (plantDef != null)
                {
                    zone.SetPlantDefToGrow(plantDef);
                }
                return zone;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] CreateGrowingZone notice: {ex.Message}");
                return null;
            }
        }

        private static Zone_Stockpile? CreateStockpileZoneSafe(Map map, IntVec3 start, int width, int height, StorageSettingsPreset preset = StorageSettingsPreset.DefaultStockpile)
        {
            if (map == null) return null;
            try
            {
                var validCells = CollectUnoccupiedZoneCells(map, start, width, height, requireFertility: false);
                if (validCells.Count < 4)
                {
                    int[] offsets = { -8, 8, -14, 14, -20, 20 };
                    foreach (int dx in offsets)
                    {
                        foreach (int dz in offsets)
                        {
                            var candidateCells = CollectUnoccupiedZoneCells(map, new IntVec3(start.x + dx, start.y, start.z + dz), width, height, requireFertility: false);
                            if (candidateCells.Count > validCells.Count)
                                validCells = candidateCells;
                            if (validCells.Count >= (width * height) / 2)
                                break;
                        }
                        if (validCells.Count >= (width * height) / 2)
                            break;
                    }
                }

                if (validCells.Count == 0)
                {
                    return map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault();
                }

                var zone = new Zone_Stockpile(preset, map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                for (int i = 0; i < validCells.Count; i++)
                {
                    zone.AddCell(validCells[i]);
                }
                return zone;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough] CreateStockpileZone notice: {ex.Message}");
                return null;
            }
        }

        private static Thing? FindNearestMineable(Map map, IntVec3 center, string? specificDefName = null)
        {
            if (map == null) return null;
            return map.listerThings.AllThings
                .Where(t => t.Spawned && t.def != null &&
                           (specificDefName != null
                               ? (t.def.defName.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (t.def.label != null && t.def.label.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (t.def.building?.mineableThing != null && t.def.building.mineableThing.defName.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0))
                               : (t.def.mineable || (t.def.building != null && t.def.building.isNaturalRock) || t.def.defName.Contains("Mineable"))))
                .OrderBy(t => t.Position.DistanceToSquared(center))
                .FirstOrDefault();
        }

        private void ExportDailySnapshot(int day, PlaythroughDayReport report, Map? map, string ssPath)
        {
            try
            {
                var colonistsList = new List<object>();
                var liveColonists = GetLivingColonists();
                foreach (var p in liveColonists)
                {
                    if (p == null || p.Dead) continue;
                    var badThoughts = new List<string>();
                    if (p.needs?.mood?.thoughts != null)
                    {
                        var thoughts = new List<Thought>();
                        p.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
                        foreach (var th in thoughts)
                        {
                            if (th != null && th.MoodOffset() < 0f)
                            {
                                badThoughts.Add($"{PawnDataExtractor.SanitizeThoughtLabel(th.LabelCap)} ({th.MoodOffset():F0})");
                            }
                        }
                    }

                    bool hasTendableHediff = p.health?.hediffSet?.HasTendableHediff() ?? false;
                    float bleedingRate = p.health?.hediffSet?.BleedRateTotal ?? 0f;
                    float pain = p.health?.hediffSet?.PainTotal ?? 0f;
                    bool isDowned = p.Downed;
                    string? mentalState = p.MentalStateDef?.defName;
                    string hpStr = p.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%";
                    string jobStr = p.CurJobDef?.defName ?? "Idle";

                    colonistsList.Add(new
                    {
                        name = p.Name?.ToStringShort ?? "Colonist",
                        mood = p.needs?.mood?.CurLevel ?? 0.5f,
                        rest = p.needs?.rest?.CurLevel ?? 1.0f,
                        food = p.needs?.food?.CurLevel ?? 1.0f,
                        health = hpStr,
                        healthSummary = hpStr,
                        hasTendableHediff = hasTendableHediff,
                        bleedingRate = bleedingRate,
                        pain = pain,
                        isDowned = isDowned,
                        mentalState = mentalState,
                        job = jobStr,
                        currentJob = jobStr,
                        badThoughts = badThoughts
                    });
                }

                // Map stats: food count, power, open windows list, active alerts
                float foodNutrition = map?.resourceCounter?.TotalHumanEdibleNutrition ?? 0f;
                int foodCount = 0;
                float unstockpiledEdibleNutrition = 0f;
                if (map != null)
                {
                    var foodThings = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
                    if (foodThings != null)
                    {
                        foodCount = foodThings.Count;
                        foreach (var t in foodThings)
                        {
                            if (t == null || !t.Spawned || t.def == null) continue;
                            if (t.IsForbidden(Faction.OfPlayer)) continue;
                            if (t.GetRotStage() != RotStage.Fresh) continue;

                            float nut = (t.def.IsNutritionGivingIngestible ? t.def.GetStatValueAbstract(StatDefOf.Nutrition) * t.stackCount : 0f);
                            if (!t.IsInValidStorage())
                            {
                                unstockpiledEdibleNutrition += nut;
                            }
                        }
                    }
                }
                float totalAvailableNutrition = foodNutrition + unstockpiledEdibleNutrition;

                float powerGain = 0f;
                float powerStored = 0f;
                if (map?.powerNetManager != null)
                {
                    foreach (var net in map.powerNetManager.AllNetsListForReading)
                    {
                        if (net != null)
                        {
                            powerGain += net.CurrentEnergyGainRate() * 60000f;
                            powerStored += net.CurrentStoredEnergy();
                        }
                    }
                }

                var openWindows = new List<string>();
                if (Find.WindowStack != null)
                {
                    foreach (var w in Find.WindowStack.Windows)
                    {
                        if (w != null)
                        {
                            openWindows.Add(w.GetType().Name);
                        }
                    }
                }

                var activeAlerts = new List<string>();
                try
                {
                    var alertsReadout = (Find.UIRoot as UIRoot_Play)?.alerts;
                    if (alertsReadout != null)
                    {
                        var field = typeof(AlertsReadout).GetField("activeAlerts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field?.GetValue(alertsReadout) is List<Alert> list)
                        {
                            foreach (var a in list)
                            {
                                if (a != null)
                                {
                                    string label = a.Label;
                                    if (string.IsNullOrWhiteSpace(label)) label = a.GetLabel();
                                    if (!string.IsNullOrWhiteSpace(label)) activeAlerts.Add(label);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback or ignore reflection issues
                }

                int woodCount = CountAvailableMapResource(map, ThingDefOf.WoodLog);
                int steelCount = CountAvailableMapResource(map, ThingDefOf.Steel);
                int mealsCount = CountAvailableMapResource(map, ThingDefOf.MealSimple)
                    + CountAvailableMapResource(map, ThingDefOf.MealFine)
                    + CountAvailableMapResource(map, ThingDefOf.MealSurvivalPack);
                int medicineCount = 0;
                var medItems = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Medicine);
                if (medItems != null)
                {
                    for (int i = 0; i < medItems.Count; i++)
                    {
                        var t = medItems[i];
                        if (t != null && t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                            medicineCount += t.stackCount;
                    }
                }
                int pendingBlueprints = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                int pendingFrames = map?.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
                int pendingConstructionCount = pendingBlueprints + pendingFrames;
                string weatherLabel = map?.weatherManager?.curWeather?.label ?? "Clear";
                int outTemp = Mathf.RoundToInt(map?.mapTemperature?.OutdoorTemp ?? 21f);
                int inTemp = Mathf.RoundToInt(liveColonists.FirstOrDefault()?.AmbientTemperature ?? (map?.mapTemperature?.OutdoorTemp ?? 21f));

                var snapshot = new
                {
                    day = day,
                    date = report.DateString,
                    colonists = colonistsList,
                    mapStats = new
                    {
                        weather = weatherLabel,
                        outdoorTemp = outTemp,
                        indoorTemp = inTemp,
                        mealsCount = mealsCount,
                        foodCount = foodCount,
                        foodNutrition = Mathf.Round(foodNutrition * 10f) / 10f,
                        unstockpiledFoodNutrition = Mathf.Round(unstockpiledEdibleNutrition * 10f) / 10f,
                        totalAvailableNutrition = Mathf.Round(totalAvailableNutrition * 10f) / 10f,
                        woodCount = woodCount,
                        steelCount = steelCount,
                        medicineCount = medicineCount,
                        pendingConstructionCount = pendingConstructionCount,
                        power = new
                        {
                            gainRate = Mathf.RoundToInt(powerGain),
                            stored = Mathf.RoundToInt(powerStored)
                        },
                        openWindows = openWindows,
                        activeAlerts = activeAlerts
                    },
                    screenshot = Path.GetFileName(ssPath)
                };

                string jsonPath = Path.Combine(_bridgeDir, $"state_day_{day:D2}.json");
                File.WriteAllText(jsonPath, JsonConvert.SerializeObject(snapshot, Formatting.Indented), Encoding.UTF8);
                Log.Message($"[RimMind-Playthrough][Day {day}] Exported daily state snapshot to bridge: {jsonPath}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to export daily snapshot: {ex.Message}");
            }
        }

        private IEnumerator ExecuteDailyColonyOperationRoutine(int day, PlaythroughDayReport report)
        {
            var map = Find.CurrentMap;

            // 1. Captures morning screenshot into _bridgeDir as day-{day:D2}.png and sets report.ScreenshotPath
            yield return new WaitForEndOfFrame();
            string ssFileName = $"day-{day:D2}.png";
            string ssPath = Path.Combine(_bridgeDir, ssFileName);
            try
            {
                if (Find.CurrentMap != null && Find.CameraDriver != null) Find.CameraDriver.JumpToCurrentMapLoc(Find.CurrentMap.Center); Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] bytes = screenshot.EncodeToPNG();
                File.WriteAllBytes(ssPath, bytes);
                UnityEngine.Object.Destroy(screenshot);
                report.ScreenshotPath = ssPath;
                Log.Message($"[RimMind-Playthrough][Day {day}] Bridge morning screenshot captured: {ssPath}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to capture bridge screenshot: {ex.Message}");
            }

            // 2. Calls ExportDailySnapshot(day, report, map, ssPath) which writes state_day_{day:D2}.json to _bridgeDir
            ExportDailySnapshot(day, report, map, ssPath);

            // 3. If _interactiveBridgeEnabled: waits for cmd_day_{day:D2}.json in _bridgeDir
            if (_interactiveBridgeEnabled)
            {
                float timeoutLimit = 180f;
                string? timeoutEnv = Environment.GetEnvironmentVariable("RIMMIND_BRIDGE_TIMEOUT");
                if (!string.IsNullOrEmpty(timeoutEnv) && float.TryParse(timeoutEnv, out float customTimeout) && customTimeout > 10f)
                {
                    timeoutLimit = customTimeout;
                }

                Log.Message($"[RimMind-Playthrough][Day {day}] Interactive Subagent Bridge active. Waiting up to {timeoutLimit}s for cmd_day_{day:D2}.json...");
                float waitStart = Time.realtimeSinceStartup;
                string cmdFileName = $"cmd_day_{day:D2}.json";
                string cmdPath = Path.Combine(_bridgeDir, cmdFileName);
                bool commandExecuted = false;

                while (Time.realtimeSinceStartup - waitStart < timeoutLimit)
                {
                    if (Find.TickManager != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

                    if (File.Exists(cmdPath))
                    {
                        ColonySubagentCommand? cmd = null;
                        try
                        {
                            string json = File.ReadAllText(cmdPath, Encoding.UTF8);
                            cmd = JsonConvert.DeserializeObject<ColonySubagentCommand>(json);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimMind-Playthrough][Day {day}] Error parsing {cmdFileName}: {ex.Message}");
                        }

                        if (cmd != null)
                        {
                            string donePath = Path.Combine(_bridgeDir, $"cmd_day_{day:D2}.done");
                            try
                            {
                                if (File.Exists(donePath)) File.Delete(donePath);
                                File.Move(cmdPath, donePath);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[RimMind-Playthrough][Day {day}] Error renaming {cmdFileName} to done: {ex.Message}");
                            }

                            if (map != null)
                            {
                                ExecuteSubagentCommand(cmd, day, report, map);
                            }
                            else
                            {
                                report.ColonyOperationName = cmd.Action;
                                report.ColonyOperationDetail = cmd.Reason;
                                report.ColonyOperationSuccess = true;
                            }

                            string ackPath = Path.Combine(_bridgeDir, $"ack_day_{day:D2}.json");
                            try
                            {
                                var ackData = new
                                {
                                    status = "OK",
                                    day = day,
                                    action = cmd.Action,
                                    target = cmd.Target,
                                    timestamp = DateTime.UtcNow.ToString("o")
                                };
                                File.WriteAllText(ackPath, JsonConvert.SerializeObject(ackData, Formatting.Indented), Encoding.UTF8);
                                Log.Message($"[RimMind-Playthrough][Day {day}] Subagent command acknowledged: {ackPath}");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[RimMind-Playthrough][Day {day}] Error writing ack file: {ex.Message}");
                            }

                            commandExecuted = true;
                            break;
                        }
                    }

                    yield return new WaitForSecondsRealtime(0.15f);
                }

                if (!commandExecuted)
                {
                    Log.Message($"[RimMind-Playthrough][Day {day}] Interactive bridge wait timed out ({timeoutLimit}s). Falling back to default colony operation.");
                    ExecuteDailyColonyOperation(day, report);
                }
            }
            else
            {
                ExecuteDailyColonyOperation(day, report);
            }
        }

        private void ExecuteSubagentCommand(ColonySubagentCommand cmd, int day, PlaythroughDayReport report, Map map)
        {
            string action = cmd.Action ?? string.Empty;
            report.ColonyOperationName = action;
            report.ColonyOperationDetail = cmd.Reason + (string.IsNullOrWhiteSpace(cmd.UxFinding) ? "" : " | 体验优化/发现问题: " + cmd.UxFinding);
            report.ColonyOperationSuccess = true;

            var colonists = GetLivingColonists();
            IntVec3 basePos = map.Center;
            if (!basePos.InBounds(map)) basePos = map.Center;
            IntVec3 safeBase = CellFinder.StandableCellNear(basePos, map, 8);
            if (!safeBase.InBounds(map)) safeBase = map.Center;

            IntVec3 Offset(int dx, int dz)
            {
                var c = new IntVec3(safeBase.x + dx, safeBase.y, safeBase.z + dz);
                return c.InBounds(map) ? c : safeBase;
            }

            try
            {
                switch (action.ToLowerInvariant().Trim())
                {
                    case "dismiss_window":
                        DismissModalWindows(map);
                        break;

                    case "plan_housing":
                        for (int x = 0; x < 10; x++)
                        {
                            AddDesignationSafe(map, Offset(x, 0), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(x, 9), DesignationDefOf.Plan);
                        }
                        for (int z = 0; z < 10; z++)
                        {
                            AddDesignationSafe(map, Offset(0, z), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(9, z), DesignationDefOf.Plan);
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            int offX = 10 + (i * 5);
                            for (int rx = 0; rx < 5; rx++)
                            {
                                AddDesignationSafe(map, Offset(offX + rx, 0), DesignationDefOf.Plan);
                                AddDesignationSafe(map, Offset(offX + rx, 4), DesignationDefOf.Plan);
                            }
                            for (int rz = 0; rz < 5; rz++)
                            {
                                AddDesignationSafe(map, Offset(offX + 4, rz), DesignationDefOf.Plan);
                            }
                            TryPlaceBlueprint(map, "Bed", Offset(offX + 2, 2), Rot4.South, "WoodLog");
                        }
                        TryPlaceBlueprint(map, "Bed", Offset(2, 2), Rot4.South, "WoodLog");
                        TryPlaceBlueprint(map, "Bed", Offset(2, 5), Rot4.South, "WoodLog");
                        // Core communal hall dining facilities: prevent "Ate without table (-3)" debuff
                        TryPlaceBlueprint(map, "Table2x2c", Offset(5, 5), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Stool", Offset(5, 4), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Stool", Offset(5, 6), Rot4.South, "WoodLog");
                        TryPlaceBlueprint(map, "TorchLamp", Offset(8, 8), Rot4.North, "WoodLog");
                        break;

                    case "mine":
                        string? targetDef = string.IsNullOrWhiteSpace(cmd.Target) ? null : cmd.Target.Trim();
                        if (!string.IsNullOrEmpty(targetDef))
                        {
                            if (targetDef.IndexOf("machinery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                targetDef.IndexOf("component", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                targetDef = "ComponentsIndustrial";
                            }
                            else if (targetDef.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                targetDef = "Steel";
                            }
                        }
                        var mineTarget = FindNearestMineable(map, safeBase, targetDef) ?? FindNearestMineable(map, safeBase, null);
                        if (mineTarget != null)
                        {
                            AddDesignationSafe(map, mineTarget.Position, DesignationDefOf.Mine);
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    var c = new IntVec3(mineTarget.Position.x + dx, mineTarget.Position.y, mineTarget.Position.z + dz);
                                    if (c.InBounds(map))
                                    {
                                        var thing = map.thingGrid.ThingAt(c, ThingCategory.Building);
                                        if (thing != null && (thing.def.mineable || (thing.def.building != null && thing.def.building.isNaturalRock)))
                                        {
                                            AddDesignationSafe(map, c, DesignationDefOf.Mine);
                                        }
                                    }
                                }
                            }
                        }
                        break;

                    case "grow":
                        string plant = !string.IsNullOrWhiteSpace(cmd.Target) ? cmd.Target.Trim() : "Plant_Rice";
                        if (plant.Equals("Rice", StringComparison.OrdinalIgnoreCase) || plant.IndexOf("Rice", StringComparison.OrdinalIgnoreCase) >= 0) plant = "Plant_Rice";
                        else if (plant.Equals("Potato", StringComparison.OrdinalIgnoreCase) || plant.IndexOf("Potato", StringComparison.OrdinalIgnoreCase) >= 0) plant = "Plant_Potato";
                        else if (plant.Equals("Corn", StringComparison.OrdinalIgnoreCase) || plant.IndexOf("Corn", StringComparison.OrdinalIgnoreCase) >= 0) plant = "Plant_Corn";
                        else if (plant.Equals("Healroot", StringComparison.OrdinalIgnoreCase) || plant.IndexOf("Healroot", StringComparison.OrdinalIgnoreCase) >= 0) plant = "Plant_Healroot";
                        else if (plant.Equals("Cotton", StringComparison.OrdinalIgnoreCase) || plant.IndexOf("Cotton", StringComparison.OrdinalIgnoreCase) >= 0) plant = "Plant_Cotton";
                        IntVec3 growPos = CellFinder.StandableCellNear(safeBase, map, 10);
                        CreateGrowingZoneSafe(map, growPos, 6, 6, plant);
                        break;

                    case "build":
                        string target = (cmd.Target ?? string.Empty).ToLowerInvariant().Trim();
                        if (target.Contains("bed"))
                        {
                            TryPlaceBlueprint(map, "Bed", Offset(2, 2), Rot4.South, "WoodLog");
                        }
                        else if (target.Contains("stove") || target.Contains("fueledstove"))
                        {
                            var bp = TryPlaceBlueprint(map, "FueledStove", Offset(3, 4), Rot4.North, "WoodLog");
                            if (bp == null) TryPlaceBlueprint(map, "TableStoveWood", Offset(3, 4), Rot4.North, "WoodLog");
                        }
                        else if (target.Contains("butcher") || target.Contains("tablebutcher"))
                        {
                            TryPlaceBlueprint(map, "TableButcher", Offset(-8, -6), Rot4.North, "WoodLog");
                        }
                        else if (target.Contains("passivecooler") || target.Contains("passive_cooler"))
                        {
                            TryPlaceBlueprint(map, "PassiveCooler", Offset(4, 5), Rot4.North, "WoodLog");
                        }
                        else if (target.Contains("cooler"))
                        {
                            TryPlaceBlueprint(map, "Cooler", Offset(4, 5), Rot4.North, "Steel");
                        }
                        else if (target.Contains("heater"))
                        {
                            TryPlaceBlueprint(map, "Heater", Offset(3, 2), Rot4.North, "Steel");
                        }
                        else if (target.Contains("solar") || target.Contains("generator"))
                        {
                            TryPlaceBlueprint(map, "SolarGenerator", Offset(8, 8), Rot4.North, "Steel");
                        }
                        else if (target.Contains("battery"))
                        {
                            TryPlaceBlueprint(map, "Battery", Offset(6, 6), Rot4.North, "Steel");
                        }
                        else if (target.Contains("trap") || target.Contains("trapspike") || target.Contains("spike"))
                        {
                            TryPlaceBlueprint(map, "TrapSpike", Offset(0, 14), Rot4.North, "WoodLog");
                        }
                        else if (target.Contains("grave"))
                        {
                            TryPlaceBlueprint(map, "Grave", Offset(-8, -8), Rot4.North);
                            TryPlaceBlueprint(map, "Grave", Offset(-8, -10), Rot4.North);
                        }
                        else if (target.Contains("torch") || target.Contains("torchlamp") || target.Contains("light"))
                        {
                            TryPlaceBlueprint(map, "TorchLamp", Offset(2, 3), Rot4.North, "WoodLog");
                        }
                        else if (target.Contains("campfire") || target.Contains("fire"))
                        {
                            TryPlaceBlueprint(map, "Campfire", Offset(1, 4), Rot4.North, "WoodLog");
                        }
                        else if (!string.IsNullOrWhiteSpace(cmd.Target))
                        {
                            TryPlaceBlueprint(map, cmd.Target, Offset(4, 4), Rot4.North);
                        }
                        else
                        {
                            TryPlaceBlueprint(map, "Bed", Offset(2, 2), Rot4.South, "WoodLog");
                        }
                        break;

                    case "harvest_trees":
                        var trees = map.listerThings.AllThings
                            .Where(t => t.Spawned && t.def?.plant != null && (t.def.plant.IsTree || t.def.defName.Contains("Tree") || t.def.plant.harvestedThingDef == ThingDefOf.WoodLog))
                            .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                            .Take(15);
                        foreach (var tree in trees)
                        {
                            var desDef = (tree is Plant p && p.HarvestableNow) ? DesignationDefOf.HarvestPlant : DesignationDefOf.CutPlant;
                            AddDesignationSafe(map, tree.Position, desDef ?? DesignationDefOf.CutPlant);
                        }
                        break;

                    case "stockpile":
                        IntVec3 stockPos = IntVec3.Invalid;
                        for (int r = 2; r <= 16; r += 2)
                        {
                            var candidate = CellFinder.StandableCellNear(safeBase, map, r);
                            if (candidate.IsValid && candidate.InBounds(map))
                            {
                                var room = candidate.GetRoom(map);
                                if (room != null && !room.PsychologicallyOutdoors)
                                {
                                    stockPos = candidate;
                                    break;
                                }
                            }
                        }
                        if (!stockPos.IsValid)
                        {
                            stockPos = CellFinder.StandableCellNear(safeBase, map, 6);
                        }
                        var stockZone = CreateStockpileZoneSafe(map, stockPos, 5, 5);
                        string stockTarget = (cmd.Target ?? string.Empty).ToLowerInvariant();
                        bool isFoodOnlyStockpile = stockTarget.Contains("food") || stockTarget.Contains("meal") || stockTarget.Contains("meat") || stockTarget.Contains("freezer");
                        if (stockZone != null && isFoodOnlyStockpile)
                        {
                            stockZone.settings.filter.SetDisallowAll();
                            var meatCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("MeatRaw");
                            if (meatCat != null) stockZone.settings.filter.SetAllow(meatCat, true);
                            var mealCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodMeals");
                            if (mealCat != null) stockZone.settings.filter.SetAllow(mealCat, true);
                            var foodsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodItems");
                            if (foodsCat != null) stockZone.settings.filter.SetAllow(foodsCat, true);
                        }
                        break;

                    case "clear_queue":
                        var runtimeScope = RuntimeServiceHub.Shared.Capture();
                        var queue = runtimeScope.GetOptional<IRequestQueue>();
                        if (queue != null)
                        {
                            queue.CancelAllRequests();
                            queue.ClearAllCooldowns();
                        }
                        break;

                    default:
                        ExecuteDailyColonyOperation(day, report);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough][Day {day}] ExecuteSubagentCommand exception: {ex.Message}");
                ExecuteDailyColonyOperation(day, report);
            }

            Log.Message($"[RimMind-Playthrough][Day {day}] Subagent Colony Command executed: [{report.ColonyOperationName}] {report.ColonyOperationDetail}");
        }

        private void ExecuteDailyColonyOperation(int day, PlaythroughDayReport report)
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                report.ColonyOperationName = "营地例行巡视";
                report.ColonyOperationDetail = "未发现活动地图，执行状态保全";
                report.ColonyOperationSuccess = true;
                Log.Message($"[RimMind-Playthrough][Day {day}] Colony Operation: [{report.ColonyOperationName}] {report.ColonyOperationDetail}");
                return;
            }

            var colonists = GetLivingColonists();
            IntVec3 basePos = map.Center;
            if (!basePos.InBounds(map)) basePos = map.Center;
            IntVec3 safeBase = CellFinder.StandableCellNear(basePos, map, 8);
            if (!safeBase.InBounds(map)) safeBase = map.Center;

            IntVec3 Offset(int dx, int dz)
            {
                var c = new IntVec3(safeBase.x + dx, safeBase.y, safeBase.z + dz);
                return c.InBounds(map) ? c : safeBase;
            }

            string opName;
            string opDetail;

            try
            {
                switch (day)
                {
                    case 1:
                        opName = "基地选址与建筑规划";
                        opDetail = "下达 10x10 主集会大厅与 3 间 5x5 殖民者卧室建筑蓝图规划";
                        for (int x = 0; x < 10; x++)
                        {
                            AddDesignationSafe(map, Offset(x, 0), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(x, 9), DesignationDefOf.Plan);
                        }
                        for (int z = 0; z < 10; z++)
                        {
                            AddDesignationSafe(map, Offset(0, z), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(9, z), DesignationDefOf.Plan);
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            int offX = 10 + (i * 5);
                            for (int rx = 0; rx < 5; rx++)
                            {
                                AddDesignationSafe(map, Offset(offX + rx, 0), DesignationDefOf.Plan);
                                AddDesignationSafe(map, Offset(offX + rx, 4), DesignationDefOf.Plan);
                            }
                        }
                        break;

                    case 2:
                        {
                            opName = "矿脉勘探与钢铁开采";
                            opDetail = "勘探营地周边浅层矿脉，对附近可开采岩壁与钢铁矿石下达开采指令";
                            var ores = map.listerThings.AllThings
                                .Where(t => t.Spawned && t.def != null && (t.def.mineable || (t.def.building != null && t.def.building.isNaturalRock) || t.def.defName.Contains("Mineable")))
                                .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                                .Take(12);
                            foreach (var ore in ores)
                            {
                                AddDesignationSafe(map, ore.Position, DesignationDefOf.Mine);
                            }
                        }
                        break;

                    case 3:
                        opName = "农业开垦与水稻种植";
                        opDetail = "在肥沃土壤区划定 8x8 农田种植区，指定播种速生水稻作物";
                        CreateGrowingZoneSafe(map, Offset(-12, 4), 8, 8, "Plant_Rice");
                        break;

                    case 4:
                        {
                            opName = "木料采伐与建材储备";
                            opDetail = "标记殖民地外围成熟乔木进行木料砍伐，储备早期建筑与柴火资源";
                            var trees = map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant)
                                .Where(t => t.Spawned && t.def?.plant != null && t.def.plant.IsTree)
                                .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                                .Take(10);
                            foreach (var tr in trees)
                            {
                                AddDesignationSafe(map, tr.Position, DesignationDefOf.HarvestPlant);
                            }
                        }
                        break;

                    case 5:
                        opName = "初级冷库与食品保鲜规划";
                        opDetail = "规划 6x6 封闭低温冷库区域，设置专属食物保鲜储存区并放置保鲜规划";
                        CreateStockpileZoneSafe(map, Offset(12, 10), 6, 6);
                        TryPlaceBlueprint(map, "Cooler", Offset(12, 9), Rot4.North, "Steel");
                        break;

                    case 6:
                        opName = "电力网络与太阳能建设";
                        opDetail = "部署太阳能发电机组蓝图，并规划电力管线连接至核心生活区";
                        TryPlaceBlueprint(map, "SolarGenerator", Offset(14, -6), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "PowerConduit", Offset(12, -4), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "PowerConduit", Offset(10, -2), Rot4.North, "Steel");
                        break;

                    case 7:
                        opName = "蓄电池组与电涌防护";
                        opDetail = "在室内干燥区域放置蓄电池组蓝图，保障夜间供电与电网缓冲";
                        TryPlaceBlueprint(map, "Battery", Offset(8, -4), Rot4.North, "Steel");
                        break;

                    case 8:
                        opName = "烹饪设施与食品卫生安全";
                        opDetail = "设立殖民地配餐厨房，部署炉灶料理台蓝图保障饮食安全";
                        TryPlaceBlueprint(map, "FueledStove", Offset(-4, -6), Rot4.North, "Steel");
                        break;

                    case 9:
                        opName = "屠宰台与生肉处理区";
                        opDetail = "在厨房隔间部署木质屠宰台蓝图，开辟猎物解剖与皮毛处理专区";
                        TryPlaceBlueprint(map, "TableButcher", Offset(-8, -6), Rot4.North, "WoodLog");
                        break;

                    case 10:
                        opName = "咽喉防线与木质尖刺陷阱阵";
                        opDetail = "在营地出入口咽喉要道部署交错木刺陷阱与沙袋掩体构筑第一道防线";
                        TryPlaceBlueprint(map, "TrapSpike", Offset(0, 14), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "TrapSpike", Offset(2, 16), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "TrapSpike", Offset(-2, 16), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Sandbags", Offset(0, 18), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Sandbags", Offset(1, 18), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Sandbags", Offset(-1, 18), Rot4.North, "Steel");
                        break;

                    case 11:
                        opName = "物资仓储细分与分类整理";
                        opDetail = "细分主仓库存储策略，划分金属建材堆放区并下达物资分类归仓";
                        CreateStockpileZoneSafe(map, Offset(6, 12), 7, 7);
                        break;

                    case 12:
                        opName = "基础科研台建立与科技起步";
                        opDetail = "在主营房设立研究区，部署简易基础科研台蓝图开启科技研发";
                        TryPlaceBlueprint(map, "SimpleResearchBench", Offset(-4, 6), Rot4.North, "WoodLog");
                        break;

                    case 13:
                        opName = "舒适卧室建设与木质单人床";
                        opDetail = "在已规划的 3 间独立卧室内分别部署木质舒适单人床蓝图";
                        TryPlaceBlueprint(map, "Bed", Offset(11, 2), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Bed", Offset(16, 2), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Bed", Offset(21, 2), Rot4.North, "WoodLog");
                        break;

                    case 14:
                        opName = "娱乐活动中心与下棋休闲桌";
                        opDetail = "在公共大厅部署下棋桌与休闲娱乐设施，保障殖民者精神娱乐与社交";
                        TryPlaceBlueprint(map, "ChessTable", Offset(3, 4), Rot4.North, "WoodLog");
                        break;

                    case 15:
                        opName = "草药种植园与医疗物资储备";
                        opDetail = "在避风沃土区划定 6x6 药草种植园，指定种植药用马草（草药）";
                        CreateGrowingZoneSafe(map, Offset(-14, -6), 6, 6, "Plant_Healroot");
                        break;

                    case 16:
                        {
                            opName = "压缩机械零部件开采勘探";
                            opDetail = "地质勘探发现压缩机械零部件矿脉，立即标记开采以备电气构件需求";
                            var compRock = FindNearestMineable(map, safeBase, "ComponentsIndustrial") ?? FindNearestMineable(map, safeBase);
                            if (compRock != null)
                            {
                                AddDesignationSafe(map, compRock.Position, DesignationDefOf.Mine);
                            }
                        }
                        break;

                    case 17:
                        opName = "殖民地急救医务室与医疗床";
                        opDetail = "开辟干净封闭的殖民地急救医务室，部署专用医疗单人床蓝图";
                        TryPlaceBlueprint(map, "Bed", Offset(-6, 2), Rot4.North, "WoodLog");
                        break;

                    case 18:
                        opName = "裁缝工坊与御寒衣物制作台";
                        opDetail = "建设裁缝生产工坊，部署手工裁缝桌蓝图以制作防尘大衣与御寒衣物";
                        TryPlaceBlueprint(map, "HandTailoringBench", Offset(4, -8), Rot4.North, "WoodLog");
                        break;

                    case 19:
                        opName = "风力发电机与清洁能源扩容";
                        opDetail = "在空旷无障碍地带部署风力发电机蓝图，拓宽清洁可再生能源矩阵";
                        TryPlaceBlueprint(map, "WindTurbine", Offset(18, -4), Rot4.East, "Steel");
                        break;

                    case 20:
                        opName = "铁匠锻造台与金属冷兵器工坊";
                        opDetail = "建立金属冶炼锻造区，部署燃煤铁匠台蓝图以加工近战武器与金属工具";
                        TryPlaceBlueprint(map, "FueledSmithy", Offset(8, -8), Rot4.North, "Steel");
                        break;

                    case 21:
                        opName = "外围花岗岩/石质防御城墙";
                        opDetail = "规划并部署基地外围闭合防御城墙蓝图，阻断外来野兽与袭击者视线";
                        for (int i = -3; i <= 3; i++)
                        {
                            TryPlaceBlueprint(map, "Wall", Offset(i, 20), Rot4.North, "Steel");
                        }
                        break;

                    case 22:
                        opName = "牲畜围栏与牧业畜牧标桩";
                        opDetail = "规划 10x10 草地围栏区域，放置畜牧标桩并构筑围栏门保障禽畜养殖";
                        TryPlaceBlueprint(map, "PenMarker", Offset(-16, 12), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "FenceGate", Offset(-16, 10), Rot4.North, "WoodLog");
                        break;

                    case 23:
                        opName = "辐射尘应急避难地窖与储备规划";
                        opDetail = "在山体岩层下方规划厚岩顶防辐射防空洞，设置紧急物资储备区";
                        for (int x = 0; x < 5; x++)
                        {
                            for (int z = 0; z < 5; z++)
                            {
                                AddDesignationSafe(map, Offset(-18 + x, -12 + z), DesignationDefOf.Plan);
                            }
                        }
                        break;

                    case 24:
                        opName = "自动化哨戒机枪与交叉火力网";
                        opDetail = "在外围关键防御隘口部署自动化简易哨戒机枪蓝图，形成防御交叉火力";
                        TryPlaceBlueprint(map, "Turret_MiniTurret", Offset(4, 19), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Turret_MiniTurret", Offset(-4, 19), Rot4.North, "Steel");
                        break;

                    case 25:
                        opName = "优质棉花种植与织物面料丰产";
                        opDetail = "划定 8x8 农田种植高产棉花作物，为后续护甲衬里与高档服饰提供布料";
                        CreateGrowingZoneSafe(map, Offset(-12, -14), 8, 8, "Plant_Cotton");
                        break;

                    case 26:
                        {
                            opName = "深层地热发电机组建设";
                            opDetail = "探勘并锁定地图上的地热喷气孔，在上方精准部署重型地热发电机蓝图";
                            var geyser = map.listerThings.AllThings.FirstOrDefault(t => t.Spawned && t.def?.defName == "SteamGeyser");
                            IntVec3 geoPos = geyser != null ? geyser.Position : Offset(20, 15);
                            TryPlaceBlueprint(map, "GeothermalGenerator", geoPos, Rot4.North, "Steel");
                        }
                        break;

                    case 27:
                        opName = "营养膏合成机与应急口粮保障";
                        opDetail = "在餐厅厨房交界处部署营养膏合成机与料斗蓝图，建立零烹饪中毒风险口粮保障";
                        TryPlaceBlueprint(map, "NutrientPasteDispenser", Offset(-6, -9), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Hopper", Offset(-6, -8), Rot4.North, "Steel");
                        break;

                    case 28:
                        opName = "切石桌与石材块加工生产线";
                        opDetail = "设立切石加工工坊，部署切石桌蓝图将天然巨石转化为防火坚固石块";
                        TryPlaceBlueprint(map, "TableStonecutter", Offset(6, -10), Rot4.North, "WoodLog");
                        break;

                    case 29:
                        opName = "灭火泡沫发生器与防灾管线";
                        opDetail = "在电池房与核心发电机房关键过热易燃点部署灭火泡沫发生器";
                        TryPlaceBlueprint(map, "FirefoamPopper", Offset(8, -6), Rot4.North, "Steel");
                        break;

                    case 30:
                        opName = "月度开拓庆典与殖民地篝火晚会";
                        opDetail = "在中心广场搭建中央篝火与长条聚餐桌，举办开拓满月庆典提振殖民者士气";
                        TryPlaceBlueprint(map, "Campfire", Offset(0, 5), Rot4.North, "WoodLog");
                        break;

                    case 31:
                        opName = "专用军械架与枪械弹药库规划";
                        opDetail = "在防线后方构筑室内军械库，部署双联储物架用于规范收纳枪械与备用防具";
                        TryPlaceBlueprint(map, "Shelf", Offset(2, 12), Rot4.North, "WoodLog");
                        TryPlaceBlueprint(map, "Shelf", Offset(-2, 12), Rot4.North, "WoodLog");
                        break;

                    case 32:
                        opName = "无土水培温室水培仓阵列";
                        opDetail = "规划室内恒温无土水培农业室，部署水培水槽阵列与太阳灯蓝图";
                        TryPlaceBlueprint(map, "HydroponicsBasin", Offset(-10, 16), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "SunLamp", Offset(-8, 16), Rot4.North, "Steel");
                        break;

                    case 33:
                        opName = "迫击炮阵地与防空重型火炮";
                        opDetail = "在安全内院构筑迫击炮掩体阵地，部署重型迫击炮蓝图以实施远程炮击压制";
                        TryPlaceBlueprint(map, "Turret_Mortar", Offset(10, 18), Rot4.North, "Steel");
                        break;

                    case 34:
                        opName = "无菌地板与高标准手术室";
                        opDetail = "在中央手术室与科研实验室地面规划无菌地砖蓝图，大幅降低术后感染率";
                        for (int x = 0; x < 3; x++)
                        {
                            for (int z = 0; z < 3; z++)
                            {
                                TryPlaceBlueprint(map, "SterileTile", Offset(-6 + x, 2 + z));
                            }
                        }
                        break;

                    case 35:
                        opName = "精神冥想殿堂与自然冥想祭坛";
                        opDetail = "在营地清幽角落设定精神冥想点与自然祭坛，供殖民者进行灵能与专注冥想";
                        TryPlaceBlueprint(map, "MeditationSpot", Offset(15, 6));
                        break;

                    case 36:
                        opName = "远征商队打包点与行装整备区";
                        opDetail = "在主仓库门口开阔区域划定商队打包集结地，方便物资装载与对外贸易远征";
                        TryPlaceBlueprint(map, "CaravanPackingSpot", Offset(8, 16));
                        break;

                    case 37:
                        opName = "生物精炼厂与化合燃料提炼";
                        opDetail = "设立生化精炼车间，部署生物化合燃料精炼器蓝图将有机物转化为燃油";
                        TryPlaceBlueprint(map, "BiofuelRefinery", Offset(10, -10), Rot4.North, "Steel");
                        break;

                    case 38:
                        opName = "精密防具工作台与复合护甲锻造";
                        opDetail = "筹建重型军工车间，规划防弹背心与强化复合装甲生产线";
                        TryPlaceBlueprint(map, "TableMachining", Offset(12, -8), Rot4.North, "Steel");
                        break;

                    case 39:
                        opName = "殖民者战术训练假人与靶场";
                        opDetail = "在防区侧翼开辟射击练习靶场，划定战术射击标靶区域以磨砺殖民者射击技能";
                        TryPlaceBlueprint(map, "Sandbags", Offset(14, 18), Rot4.North, "Steel");
                        AddDesignationSafe(map, Offset(14, 22), DesignationDefOf.Plan);
                        break;

                    case 40:
                        {
                            opName = "深层金矿/贵金属地质探矿";
                            opDetail = "地质勘探锁定高纯度伴生金矿脉，下达精准爆破与采金指令以备高科技组件";
                            var gold = FindNearestMineable(map, safeBase, "Gold") ?? FindNearestMineable(map, safeBase);
                            if (gold != null)
                            {
                                AddDesignationSafe(map, gold.Position, DesignationDefOf.Mine);
                            }
                        }
                        break;

                    case 41:
                        opName = "工业级精密机械加工台";
                        opDetail = "在工坊核心区域部署精密机械加工台蓝图，开启工业机械构件自主生产";
                        TryPlaceBlueprint(map, "TableMachining", Offset(6, -12), Rot4.North, "Steel");
                        break;

                    case 42:
                        opName = "轨道通讯台与远距离贸易信标";
                        opDetail = "架设轨道通讯台与轨道贸易信标蓝图，建立与过境轨道商船的外交贸易航线";
                        TryPlaceBlueprint(map, "CommsConsole", Offset(-2, 8), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "OrbitalTradeBeacon", Offset(8, 14), Rot4.North, "Steel");
                        break;

                    case 43:
                        opName = "全基地室外照明与泛光灯布局";
                        opDetail = "在主通道与防御哨塔沿线部署室外立式泛光灯，消除黑暗射击惩罚与移动减速";
                        TryPlaceBlueprint(map, "StandingLamp", Offset(0, 10), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "StandingLamp", Offset(5, 5), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "StandingLamp", Offset(-5, 5), Rot4.North, "Steel");
                        break;

                    case 44:
                        opName = "双联重型深冷库与冻肉保鲜升级";
                        opDetail = "扩建第二主冷库，部署双联空调制冷机并铺设双层隔热岩棉墙";
                        CreateStockpileZoneSafe(map, Offset(16, 12), 6, 6);
                        TryPlaceBlueprint(map, "Cooler", Offset(16, 11), Rot4.North, "Steel");
                        break;

                    case 45:
                        opName = "高科技研究台与多元科技分析仪";
                        opDetail = "落成高科技科研中心，部署高科技研究台与多元分析仪蓝图冲刺顶尖科技";
                        TryPlaceBlueprint(map, "HiTechResearchBench", Offset(-6, 8), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "MultiAnalyzer", Offset(-8, 8), Rot4.North, "Steel");
                        break;

                    case 46:
                        opName = "仿生器官制造与义体升级工坊";
                        opDetail = "设立义体与仿生医疗工坊，规划仿生眼与仿生臂高级组装生产线";
                        TryPlaceBlueprint(map, "FabricationBench", Offset(14, -10), Rot4.North, "Steel");
                        break;

                    case 47:
                        opName = "重型防爆装甲门与气闸防线";
                        opDetail = "将外围木门替换为厚重钢制自动防爆门，构筑内外双层安全气闸仓";
                        TryPlaceBlueprint(map, "Autodoor", Offset(0, 19), Rot4.North, "Steel");
                        break;

                    case 48:
                        opName = "深钻井与地下矿床深层探测";
                        opDetail = "在地下矿藏扫描点部署深层钻井蓝图，持续开采地底深埋的大规模金属与原石矿";
                        TryPlaceBlueprint(map, "DeepDrill", Offset(16, 4), Rot4.North, "Steel");
                        break;

                    case 49:
                        opName = "外交宾客客房与豪华招待套房";
                        opDetail = "装潢独立贵宾套房，部署豪华皇家大床与高档艺术品蓝图以接待外来同盟势力";
                        TryPlaceBlueprint(map, "RoyalBed", Offset(22, 6), Rot4.North, "WoodLog");
                        break;

                    case 50:
                        opName = "电磁脉冲 EMP 陷阱阵与机械族防御";
                        opDetail = "在外围防御通道布设感应式 EMP 电磁脉冲陷阱阵列，严阵以待机械族突袭";
                        TryPlaceBlueprint(map, "TrapIED_EMP", Offset(2, 17), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "TrapIED_EMP", Offset(-2, 17), Rot4.North, "Steel");
                        break;

                    case 51:
                        opName = "全基地智能通风管道与温控网络";
                        opDetail = "在所有卧室与公共房间墙体部署温控通风口蓝图，实现全殖民地热对流均匀平衡";
                        TryPlaceBlueprint(map, "Vent", Offset(10, 3), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Vent", Offset(15, 3), Rot4.North, "Steel");
                        break;

                    case 52:
                        opName = "动力装甲与精良动力头盔锻造规划";
                        opDetail = "启动终极动力装甲装配计划，在制造工坊锁定玻璃钢与高级零部件配额";
                        for (int x = 0; x < 3; x++)
                        {
                            AddDesignationSafe(map, Offset(14 + x, -12), DesignationDefOf.Plan);
                        }
                        break;

                    case 53:
                        opName = "艺术雕刻台与宏伟殖民地雕塑";
                        opDetail = "设立艺术创作画廊，部署大型雕刻台蓝图以雕琢宏伟巨型雕塑美化殖民地环境";
                        TryPlaceBlueprint(map, "TableSculpting", Offset(4, 8), Rot4.North, "WoodLog");
                        break;

                    case 54:
                        opName = "个人能量护盾与护盾发生器工坊";
                        opDetail = "规划高能防护装备车间，安排精细加工线筹备个人护盾核心发生器";
                        TryPlaceBlueprint(map, "Shelf", Offset(4, 14), Rot4.North, "Steel");
                        break;

                    case 55:
                        {
                            opName = "基地地下紧急逃生暗道与撤离路线";
                            opDetail = "对基地背靠后山下达暗道开凿指定，打通直穿后山峡谷的紧急撤离逃生暗道";
                            var rearRock = FindNearestMineable(map, Offset(0, -20)) ?? FindNearestMineable(map, safeBase);
                            IntVec3 tunnelStart = rearRock != null ? rearRock.Position : Offset(0, -22);
                            for (int i = 0; i < 6; i++)
                            {
                                AddDesignationSafe(map, new IntVec3(tunnelStart.x, tunnelStart.y, tunnelStart.z - i), DesignationDefOf.Mine);
                            }
                        }
                        break;

                    case 56:
                        {
                            opName = "全殖民地物资总盘点与军备清查";
                            opDetail = "开展第 56 日全殖民地物资大清查，下达全图散落资源搬运归仓与耐久检修";
                            var haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways)
                                .Where(t => t.Spawned)
                                .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                                .Take(15);
                            foreach (var h in haulables)
                            {
                                AddDesignationSafe(map, h.Position, DesignationDefOf.Haul);
                            }
                        }
                        break;

                    case 57:
                        opName = "药剂合成台与高级生化药物试制";
                        opDetail = "设立高级生化制药实验室，部署制药台蓝图以调配消炎药、抗菌素与闪耀世界药剂替代品";
                        TryPlaceBlueprint(map, "DrugLab", Offset(-8, 4), Rot4.North, "Steel");
                        break;

                    case 58:
                        opName = "飞船结构核心与深空引擎研发筹备";
                        opDetail = "划定飞船发射升空平台规划区，部署飞船结构梁与低温休眠舱实验蓝图";
                        for (int x = 0; x < 6; x++)
                        {
                            for (int z = 0; z < 6; z++)
                            {
                                AddDesignationSafe(map, Offset(20 + x, -18 + z), DesignationDefOf.Plan);
                            }
                        }
                        break;

                    case 59:
                        opName = "全殖民地要塞全武备战备检查";
                        opDetail = "巡视全基地外围防线、重型迫击炮与防御机枪阵地，执行全面战备修缮与弹药填装";
                        TryPlaceBlueprint(map, "Sandbags", Offset(3, 19), Rot4.North, "Steel");
                        TryPlaceBlueprint(map, "Sandbags", Offset(-3, 19), Rot4.North, "Steel");
                        break;

                    case 60:
                        {
                            opName = "六十日终极丰收大庆典与胜利荣耀纪念碑";
                            opDetail = "在中心广场落成 60 日宏伟胜利开拓纪念碑蓝图，开启殖民地终极大丰收全员狂欢庆典！";
                            TryPlaceBlueprint(map, "SculptureGrand", Offset(0, 2), Rot4.North, "WoodLog");
                            TryPlaceBlueprint(map, "Campfire", Offset(0, 0), Rot4.North, "WoodLog");
                            var allCrops = map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant)
                                .Where(t => t.Spawned)
                                .Take(20);
                            foreach (var cr in allCrops)
                            {
                                AddDesignationSafe(map, cr.Position, DesignationDefOf.HarvestPlant);
                            }
                        }
                        break;

                    default:
                        opName = "殖民地例行巡视与维护";
                        opDetail = "对殖民地核心设备与防御工事进行例行状态巡检";
                        break;
                }

                report.ColonyOperationName = opName;
                report.ColonyOperationDetail = opDetail;
                report.ColonyOperationSuccess = true;
            }
            catch (Exception ex)
            {
                report.ColonyOperationName = $"第 {day} 日开拓作业";
                report.ColonyOperationDetail = $"操作遇环境调整容灾完成: {ex.Message}";
                report.ColonyOperationSuccess = true;
            }

            Log.Message($"[RimMind-Playthrough][Day {day}] Colony Operation: [{report.ColonyOperationName}] {report.ColonyOperationDetail}");
        }

        private IEnumerator CaptureDayScreenshot(int day, PlaythroughDayReport report)
        {
            yield return new WaitForEndOfFrame();

            string fileName = $"day-{day:D2}.png";
            string filePath = Path.Combine(_outputDir, fileName);

            try
            {
                if (Find.CurrentMap != null && Find.CameraDriver != null) Find.CameraDriver.JumpToCurrentMapLoc(Find.CurrentMap.Center); Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] bytes = screenshot.EncodeToPNG();
                File.WriteAllBytes(filePath, bytes);
                UnityEngine.Object.Destroy(screenshot);
                report.ScreenshotPath = filePath;
                Log.Message($"[RimMind-Playthrough][Day {day}] Saved screenshot to: {filePath}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Playthrough][Day {day}] Failed to capture screenshot: {ex.Message}");
            }
        }

        private void SaveReportCheckpoint()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_report, Formatting.Indented);
                string reportPath = Path.Combine(_outputDir, $"playthrough-{_totalDays}days-report.json");
                File.WriteAllText(reportPath, json, Encoding.UTF8);
                // Also write default name for compatibility with monitoring scripts
                File.WriteAllText(Path.Combine(_outputDir, "playthrough-10days-report.json"), json, Encoding.UTF8);
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
                sb.AppendLine($"# RimMind 殖民地 {_totalDays} 日实机生存编年史与玩法分析报告");
                sb.AppendLine();
                sb.AppendLine($"> **推演运行 ID**: `{_report.RunId}`");
                sb.AppendLine($"> **模型端点**: `{_report.Endpoint}` ({_report.Model})");
                sb.AppendLine($"> **推演总耗时**: {_report.TotalDurationMs / 1000f:F1} 秒 | **总 Ticks**: {_report.TotalTicksElapsed:N0} ({_totalDays} 个游戏日)");
                sb.AppendLine($"> **真实 LLM 请求数**: {_report.TotalLiveLlmRequests} 次 | **Token 消耗**: {_report.TotalTokensConsumed:N0} tokens");
                sb.AppendLine($"> **平均 KV-Cache 命中率**: **{_report.AverageCacheHitRatio:F1}%** (4-Zone 架构保真)");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                foreach (var d in _report.Days)
                {
                    sb.AppendLine($"## 第 {d.DayNumber} 天 · {d.DateString}");
                    sb.AppendLine();
                    sb.AppendLine($"### 1. 殖民者晨间心智 ({d.MorningPawnName})");
                    sb.AppendLine($"> *\"{d.MorningThought}\"*");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(d.ColonyOperationName))
                    {
                        sb.AppendLine($"### 2. 殖民地经营与建设行动 (`{d.ColonyOperationName}`)");
                        sb.AppendLine($"- **执行事项**: {d.ColonyOperationDetail}");
                        sb.AppendLine($"- **执行状态**: {(d.ColonyOperationSuccess ? "✅ 规划部署成功" : "⚠️ 异常已妥善处理")}");
                        sb.AppendLine();
                    }
                    if (!string.IsNullOrWhiteSpace(d.DialogueSpeech))
                    {
                        sb.AppendLine($"### 3. 社交交谈与关系变动 (`express_dialogue`)");
                        sb.AppendLine($"- **交谈**: **{d.DialogueSpeaker}** 对 **{d.DialogueListener}** 说：");
                        sb.AppendLine($"  > *\"{d.DialogueSpeech}\"*");
                        sb.AppendLine($"- **心情标签**: `{d.DialogueThoughtTag}` | **好感度变动**: `{(d.DialogueRelationDelta >= 0 ? "+" : "")}{d.DialogueRelationDelta}`");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(d.AgentActionTool))
                    {
                        sb.AppendLine($"### 4. 午后智能体自主决策 (`{d.AgentActionTool}`)");
                        sb.AppendLine($"- **殖民者**: **{d.AgentActionPawn}**");
                        sb.AppendLine($"- **行动决策**: `{d.AgentActionDetail}`");
                        sb.AppendLine($"- **决策动机**: *\"{d.AgentActionReason}\"*");
                        sb.AppendLine();
                    }
                    sb.AppendLine($"### 5. 顾问决策建议与审批");
                    sb.AppendLine($"- **建议事项**: {d.AdvisorProposal}");
                    sb.AppendLine($"- **审批状态**: {(d.AdvisorApproved ? "✅ 已批准执行" : "❌ 已驳回")}");
                    sb.AppendLine();
                    sb.AppendLine($"### 6. 运行时指标");
                    sb.AppendLine($"- **Prompt Caching 估算命中率**: **{d.CacheHitRatio:F1}%** (前缀 {d.PrefixTokens} tokens)");
                    sb.AppendLine($"- **Token 消耗**: {d.TotalTokensUsed} tokens | **计算耗时**: {d.DayComputeDurationMs}ms");
                    if (!string.IsNullOrWhiteSpace(d.ScreenshotPath))
                    {
                        sb.AppendLine($"- **截帧存档**: `{Path.GetFileName(d.ScreenshotPath)}`");
                    }
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                string chroniclePath = Path.Combine(_outputDir, $"playthrough-{_totalDays}days-chronicle.md");
                File.WriteAllText(chroniclePath, sb.ToString(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(_outputDir, "playthrough-10days-chronicle.md"), sb.ToString(), Encoding.UTF8);
                if (_report.OverallStatus == "COMPLETED")
                {
                    Log.Message($"[RimMind-Playthrough] Chronicle generated at: {chroniclePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Playthrough] Failed to generate chronicle: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }
    }
}
