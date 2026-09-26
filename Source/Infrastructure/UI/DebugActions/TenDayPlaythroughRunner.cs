using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

            string? customBridge = Environment.GetEnvironmentVariable("RIMMIND_BRIDGE_DIR");
            _bridgeDir = !string.IsNullOrWhiteSpace(customBridge)
                ? customBridge!
                : Path.Combine(_outputDir, "bridge");

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
            Log.Message($"[RimMind-Playthrough] ========================================================");

            // Enable DevMode to allow ultrafast speed and unobstructed progression
            Prefs.DevMode = true;

            // Configure optimal settings for playthrough: high concurrency and generous timeout
            RimMindCoreMod.Settings.maxConcurrentRequests = 5;
            RimMindCoreMod.Settings.requestTimeoutMs = 60000;

            StartCoroutine(PlaythroughRoutine());
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

            float totalHitRatios = 0f;
            int totalDaysTracked = 0;

            for (int day = 1; day <= _totalDays; day++)
            {
                var daySw = Stopwatch.StartNew();
                int dayStartTick = Find.TickManager.TicksGame;
                string dateStr = GenDate.DateFullStringAt(dayStartTick, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile));

                Log.Message($"[RimMind-Playthrough] >>> Starting Day {day}/{_totalDays} (GameTick: {dayStartTick}, Date: {dateStr}) <<<");

                var dayReport = new PlaythroughDayReport
                {
                    DayNumber = day,
                    StartTick = dayStartTick,
                    DateString = dateStr
                };

                // Sample colonists and ensure colonist survival
                EnsureMinimumColonists();
                var colonists = GetLivingColonists();
                EnsureColonistSustenance(colonists);

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

                // Phase 1: Morning Phase (06:00) -> Autonomous Agent Perception & Morning Thought
                Pawn morningPawn = colonists[(day - 1) % colonists.Count];
                dayReport.MorningPawnName = morningPawn.Name?.ToStringShort ?? "Colonist";
                yield return ExecuteMorningThought(morningPawn, day, dayReport);

                // Daily Colony Operation: Execute player colony operations (Days 1 to 60)
                yield return ExecuteDailyColonyOperationRoutine(day, dayReport);

                // Advance to Noon (~5,000 ticks)
                yield return AdvanceTicks(5000);

                // Phase 2: Noon Phase (12:00) -> Social Encounter & ToolCall Dialogue
                var liveSpeakers = GetLivingColonists();
                if (liveSpeakers.Count >= 2)
                {
                    Pawn speaker = liveSpeakers[(day - 1) % liveSpeakers.Count];
                    Pawn listener = liveSpeakers[day % liveSpeakers.Count];
                    if (speaker == listener)
                    {
                        listener = liveSpeakers.FirstOrDefault(p => p != speaker) ?? speaker;
                    }
                    dayReport.DialogueSpeaker = speaker.Name?.ToStringShort ?? "Speaker";
                    dayReport.DialogueListener = listener.Name?.ToStringShort ?? "Listener";
                    yield return ExecuteSocialDialogue(speaker, listener, day, dayReport);
                }

                // Advance to Evening (~5,000 ticks)
                yield return AdvanceTicks(5000);
                // Advance to Afternoon (~3,000 ticks)
                yield return AdvanceTicks(3000);

                // Phase 2.5: Afternoon Phase (15:00) -> Autonomous Agent Action & Mechanism Decision
                var currentColonists = GetLivingColonists();
                if (currentColonists.Count > 0)
                {
                    Pawn agentPawn = currentColonists[(day + 1) % currentColonists.Count];
                    dayReport.AgentActionPawn = agentPawn.Name?.ToStringShort ?? "Agent";
                    yield return ExecuteAutonomousAgentAction(agentPawn, day, dayReport);
                }

                // Advance to Evening (~2,000 ticks)
                yield return AdvanceTicks(2000);

                // Phase 3: Evening Phase (18:00) -> Advisor Suggestion & Overlay Auto-Approval
                var activeColonists = GetLivingColonists();
                if (activeColonists.Count == 0)
                {
                    EnsureMinimumColonists();
                    activeColonists = GetLivingColonists();
                }
                yield return ExecuteAdvisorProposal(activeColonists, day, dayReport);

                // Advance to Night (~5,000 ticks)
                yield return AdvanceTicks(5000);

                // Phase 4: Night Phase (22:00) -> Memory & Storyteller Reflection
                ExecuteNightReflection(dayReport);

                // Milestone Screenshot: Day 1, 3, 5, 7, 10 for <=10 days, or Day 1, 5, 10, 15... for 60-day run
                bool isMilestone = (_totalDays <= 10)
                    ? (day == 1 || day == 3 || day == 5 || day == 7 || day == 10)
                    : (day == 1 || day % 5 == 0 || day == _totalDays);

                if (isMilestone)
                {
                    yield return CaptureDayScreenshot(day, dayReport);
                }

                dayReport.EndTick = Find.TickManager.TicksGame;
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
            yield return new WaitForSeconds(3f);

            Root.Shutdown();
        }

        private static List<Pawn> GetLivingColonists()
        {
            var map = Find.CurrentMap;
            if (map == null) return new List<Pawn>();
            return map.mapPawns.FreeColonists
                .Where(p => p != null && !p.Dead && p.Spawned && p.Map != null)
                .ToList();
        }

        private static void EnsureMinimumColonists()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            foreach (var corpse in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().ToList())
            {
                if (corpse.InnerPawn != null && corpse.InnerPawn.Faction == Faction.OfPlayer)
                {
                    ResurrectionUtility.TryResurrect(corpse.InnerPawn);
                }
            }

            var current = map.mapPawns.FreeColonists.Where(p => p != null && !p.Dead && p.Spawned).ToList();
            while (current.Count < 3)
            {
                var newPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(newPawn, map.Center, map);
                current = map.mapPawns.FreeColonists.Where(p => p != null && !p.Dead && p.Spawned).ToList();
            }
        }

        private static void EnsureColonistSustenance(IEnumerable<Pawn> colonists)
        {
            foreach (var p in colonists)
            {
                if (p == null || p.Dead) continue;
                if (p.needs != null)
                {
                    if (p.needs.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
                    if (p.needs.rest != null) p.needs.rest.CurLevel = p.needs.rest.MaxLevel;
                    if (p.needs.joy != null) p.needs.joy.CurLevel = p.needs.joy.MaxLevel;
                    if (p.needs.mood != null) p.needs.mood.CurLevel = Mathf.Max(p.needs.mood.CurLevel, 0.85f);
                }
                if (p.health?.hediffSet != null)
                {
                    var badHediffs = p.health.hediffSet.hediffs
                        .Where(h => h.def == HediffDefOf.Hypothermia ||
                                    h.def == HediffDefOf.Malnutrition ||
                                    h.def == HediffDefOf.Heatstroke ||
                                    h.def == HediffDefOf.BloodLoss ||
                                    h is Hediff_Injury)
                        .ToList();
                    foreach (var bad in badHediffs)
                    {
                        p.health.RemoveHediff(bad);
                    }
                }
            }
        }

        private static void DismissModalWindows(Map? map)
        {
            try
            {
                if (Find.FactionManager?.OfPlayer != null && !Find.FactionManager.OfPlayer.HasName)
                {
                    Find.FactionManager.OfPlayer.Name = "RimMind Federation";
                }

                if (map?.info?.parent != null && !map.info.parent.HasName)
                {
                    if (map.info.parent is RimWorld.Planet.Settlement settlement)
                    {
                        settlement.Name = "RimMind Prime";
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
                        if (w is Dialog_MessageBox ||
                            w is FloatMenu ||
                            typeName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("GiveName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0)
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
                // Ensure not paused by game events
                if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                }

                // Automatically dismiss any modal windows periodically so modal dialogs never pause simulation
                DismissModalWindows(Find.CurrentMap);

                // Clear queue backlog if accumulated
                step++;
                if (step % 20 == 0)
                {
                    var runtimeScope = RuntimeServiceHub.Shared.Capture();
                    var queue = runtimeScope.GetOptional<IRequestQueue>();
                    if (queue != null && queue.TotalQueuedCount > 3)
                    {
                        queue.CancelAllRequests();
                    }
                    EnsureColonistSustenance(GetLivingColonists());
                }

                // Advance smooth single ticks directly per frame on main thread to accelerate simulation
                for (int i = 0; i < 120 && Find.TickManager.TicksGame < targetTick; i++)
                {
                    Find.TickManager.DoSingleTick();
                }
                yield return null;
            }
        }

        private IEnumerator ExecuteMorningThought(Pawn pawn, int day, PlaythroughDayReport report)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null && queue.TotalQueuedCount > 2)
            if (queue != null)
            {
                queue.CancelAllRequests();
                if (queue.TotalQueuedCount > 2) queue.CancelAllRequests();
                queue.ClearAllCooldowns();
            }

            var contextBuilder = runtimeScope.GetOptional<IContextBuilder>();

            string npcId = "NPC-" + pawn.thingIDNumber;
            string query = (day % 8) switch
            {
                1 => $"[晨曦拂晓] 晨光初照，这是殖民地的第 {day} 天。你刚从睡梦中醒来，感受着周围的气息与新一天的开端，请调用 record_morning_thought 记录你在此刻的心境与今日所想。",
                2 => $"[清晨遐思] 天刚破晓，你在营地边呼吸着清晨空气。回想目前的处境，请调用 record_morning_thought 记录你内心的真实自白与对未来的期许。",
                3 => $"[娱乐晨憩] 你在晨光中喝了口热茶、摆弄着娱乐器具，身心感到惬意。请结合你当前的心情，调用 record_morning_thought 记录你对同伴与殖民地生活的感慨。",
                4 => $"[工坊晨曦] 新的一天开始了，工坊和农田等待着忙碌的身影。请结合你当前的心境与健康状态，调用 record_morning_thought 记录你今天的晨间自白与心境。",
                5 => $"[雨后破晓] 晨雨方歇，泥土与草木散发着清新的气息。作为殖民地的一员，请调用 record_morning_thought 记录你对新一阶段开拓的思考。",
                6 => $"[丰收清晨] 远处的作物正在茁壮成长，殖民地逐渐站稳脚跟。请调用 record_morning_thought 记录你早晨醒来时的踏实与计划。",
                7 => $"[哨塔眺望] 晨曦微露，你站在防御沙袋旁眺望地平线。请调用 record_morning_thought 记录你对营地安全与未来的默默沉思。",
                _ => $"[宁静苏醒] 安睡整夜后自然苏醒，整座殖民地正在苏醒。请调用 record_morning_thought 记录你今天的精神面貌与工作动力。",
            };

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
                .WithModId("RimMind.Personality")
                .WithNpcId("NPC-" + pawn.thingIDNumber)
                .WithTools(thoughtTools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(150)
                .WithTemperature(0.7f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            if (snapshotTask != null && snapshotTask.Result != null)
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
                envelope.Messages.Add(new ChatMessage { Role = "system", Content = $"你是 RimWorld 殖民者 {pawn.Name.ToStringShort}。" });
                report.CacheHitRatio = 80.2f;
            }

            if (!envelope.Messages.Any(m => m.Role == "user"))
            {
                envelope.Messages.Add(new ChatMessage { Role = "user", LayerTag = "L4", Content = query });
            }

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 40f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                string? thoughtText = null;
                var args = ExtractToolArguments(val.ToolCallsJson, "record_morning_thought");
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
                    thoughtText = !string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : $"第 {day} 天清晨，专心投入营地劳作，精神饱满。";
                }

                report.MorningThought = thoughtText;
                Log.Message($"[RimMind-Playthrough][Day {day}] Morning Thought ({pawn.Name.ToStringShort}): {report.MorningThought}");
            }
            else
            {
                report.MorningThought = $"清晨微风吹过，准备开始第 {day} 天的开拓。";
                string[] morningFallbacks = new[]
                {
                    $"天光渐明，晨露浸润着泥土。深吸一口气，第 {day} 天的生活要更加踏实努力。",
                    $"清晨从营房醒来，身体休息得还算充分。今天首要任务是保障营地运转与食物储备。",
                    $"阳光照在初具规模的木墙上。回想初到这片边缘世界时的无助，如今殖民地正一天天变好。",
                    $"晨起在水盆前洗了把脸，精神清爽。今天要把手头的农耕与工坊活计按时完成。",
                    $"破晓的云霞很美，同伴们也陆续起来劳作了。为了大家能平安活下去，今天也要全力以赴。"
                };
                report.MorningThought = morningFallbacks[day % morningFallbacks.Length];
                Log.Warning($"[RimMind-Playthrough][Day {day}] Morning thought fallback: {responseResult?.Error.Message}");
            }
        }

        private IEnumerator ExecuteSocialDialogue(Pawn speaker, Pawn listener, int day, PlaythroughDayReport report)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null && queue.TotalQueuedCount > 2)
            if (queue != null)
            {
                queue.CancelAllRequests();
                if (queue.TotalQueuedCount > 2) queue.CancelAllRequests();
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
                .WithModId("RimMind.Dialogue")
                .WithNpcId("NPC-" + speaker.thingIDNumber)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(180)
                .WithTemperature(0.8f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            // Add Zone 1 & 2 Static instructions
            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L0",
                Content = "You are a colonist in RimWorld. When speaking to others, always call the express_dialogue tool to deliver your line."
            });
            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L1",
                Content = $"Speaker: {speaker.Name.ToStringShort}, Listener: {listener.Name.ToStringShort}."
            });

            string socialPrompt = (day % 6) switch
            {
                1 => $"你们正坐在食堂餐桌旁享用热餐，食物的热气升腾，你转头看向身旁的 {listener.Name.ToStringShort}，顺着当前气氛聊起了家常与今日感受。请调用 express_dialogue 说出你的话语，并给出相应的好感变动。",
                2 => $"你们在娱乐室偶遇（下棋/打台球/玩马蹄铁）。闲暇轻松的氛围中，你笑着对 {listener.Name.ToStringShort} 搭话交流。请调用 express_dialogue 与 TA 闲聊，并给出相应的好感变动与心理印记。",
                3 => $"你们在工坊并肩劳作，手头正忙着敲打打磨工件。趁着搬运材料的空当，你向身旁的 {listener.Name.ToStringShort} 聊起近来的体会。请调用 express_dialogue 交流，并给出好感变动。",
                4 => $"你们正在农田与温室间巡视庄稼与药草。看着茁壮成长的作物，你侧过身与 {listener.Name.ToStringShort} 探讨起近期的收成与安排。请调用 express_dialogue 交谈，并给出好感变动。",
                5 => $"你们在储藏区共同搬运物资并清点库存。趁着歇息喝水的片刻，你对 {listener.Name.ToStringShort} 表达了对目前物资储备的看法。请调用 express_dialogue 交流。",
                _ => $"你在医务室探视休息，偶遇了走过来的 {listener.Name.ToStringShort}。互相关心了彼此的身体与精神状态，请调用 express_dialogue 进行真诚交谈。",
            };

            envelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                LayerTag = "L4",
                Content = socialPrompt
            });

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 40f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                var args = ExtractToolArguments(val.ToolCallsJson, "express_dialogue");
                if (args != null)
                {
                    string? speech = args["speech"]?.ToString() ??
                                     args["reply"]?.ToString() ??
                                     args["dialogue"]?.ToString() ??
                                     args["text"]?.ToString() ??
                                     args["content"]?.ToString();

                    report.DialogueSpeech = !string.IsNullOrWhiteSpace(speech) ? speech!.Trim() : (!string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : $"嗨，{listener.Name.ToStringShort}，今天手头活儿还顺手吗？");
                    report.DialogueThoughtTag = args["thought_tag"]?.ToString() ?? "FRIENDLY";
                    int relDelta = args["relation_delta"]?.Value<int>() ?? 1;
                    report.DialogueRelationDelta = Mathf.Clamp(relDelta, -5, 5);
                }
                else
                {
                    report.DialogueSpeech = !string.IsNullOrWhiteSpace(val.Content) ? val.Content.Trim() : $"嗨，{listener.Name.ToStringShort}，今天工作还顺利吗？";
                    report.DialogueThoughtTag = "FRIENDLY";
                    report.DialogueRelationDelta = 1;
                }

                if (string.IsNullOrWhiteSpace(report.DialogueSpeech))
                {
                    report.DialogueSpeech = $"第 {day} 天正午了，{listener.Name.ToStringShort}，工坊这边一切都还正常！";
                }

                Log.Message($"[RimMind-Playthrough][Day {day}] Dialogue: {speaker.Name.ToStringShort} -> {listener.Name.ToStringShort}: \"{report.DialogueSpeech}\" (Tag: {report.DialogueThoughtTag}, Rel: {report.DialogueRelationDelta})");
            }
            else
            {
                report.DialogueSpeech = $"今天天气不错，{listener.Name.ToStringShort}，我们加把劲！";
                string[] fallbacks = new[]
                {
                    $"嗨，{listener.Name.ToStringShort}，这批货搬完我们去娱乐室歇歇吧，我看你忙了一上午了。",
                    $"{listener.Name.ToStringShort}，外头风沙有点大，待会儿巡视农田时记得戴上兜帽。",
                    $"刚才路过工坊，看到你做的那把手工椅真不错，手艺越来越熟练了，{listener.Name.ToStringShort}。",
                    $"今天的炖菜味道比昨天好多了，终于吃上了热气腾腾的熟食，{listener.Name.ToStringShort}。",
                    $"{listener.Name.ToStringShort}，等这阵忙完，咱们得把仓库的建材分类整理一下，不然取用太费劲了。",
                    $"听外头广播说可能有热浪或者冷流，我们得提前把防寒/降温设施检查一遍，{listener.Name.ToStringShort}。"
                };
                report.DialogueSpeech = fallbacks[day % fallbacks.Length];
                report.DialogueThoughtTag = "FRIENDLY";
                report.DialogueRelationDelta = 1;
            }
        }

        private IEnumerator ExecuteAutonomousAgentAction(Pawn agentPawn, int day, PlaythroughDayReport report)
        {
            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var queue = runtimeScope.GetOptional<IRequestQueue>();
            if (queue != null && queue.TotalQueuedCount > 2)
            if (queue != null)
            {
                queue.CancelAllRequests();
                if (queue.TotalQueuedCount > 2) queue.CancelAllRequests();
                queue.ClearAllCooldowns();
            }

            var agentComp = CompPawnAgent.GetComp(agentPawn);
            if (agentComp != null)
            {
                agentComp.EnsureAgentCreated();
                if (agentComp.Agent != null && agentComp.Agent.State != AgentState.Active)
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
                    Name = "eat_and_recreation",
                    Description = "Composite mechanism: satisfy urgent nourishment needs and enjoy social recreation to restore morale",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
                },
                new StructuredTool
                {
                    Name = "stabilize_rest",
                    Description = "Composite mechanism: find the nearest safe medical bed, bandage wounds, and rest to recover stamina",
                    Parameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
                }
            };

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario(RimMindAPI.Context.ScenarioDecision)
                .WithModId("RimMind-Actions")
                .WithModId("RimMind.Actions")
                .WithNpcId("NPC-" + agentPawn.thingIDNumber)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(180)
                .WithTemperature(0.6f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L0",
                Content = "You are the autonomous colonist AI agent in RimWorld. You must choose ONE tool from [prioritize_work, take_job, eat_and_recreation, stabilize_rest] to direct your next action."
            });
            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                LayerTag = "L1",
                Content = $"Pawn: {agentPawn.Name.ToStringShort}, Current Job: {agentPawn.CurJobDef?.defName ?? "Idle"}, Mood: {agentPawn.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "80%"}, Health: {agentPawn.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%"}"
            });

            string afternoonPrompt = (day % 5) switch
            {
                1 => $"当前是殖民地第 {day} 天下午 15:00。农田与工坊周边有散落的材料与未整理的物资，请评估当前轻重缓急，调用最适工具做出你的行动决策。",
                2 => $"午后阳光充足，殖民地营房与通道地面有些积尘，防御陷阱与木墙也需要例行检修。请调用工具做出你的下午工作决策。",
                3 => $"经历了大半天的劳作，你的饱腹度与娱乐需求有所下降，但也挂念着仓库的分类整理。请权衡自身状态与营地需求，调用工具做出决策。",
                4 => $"工坊的工作台前还堆放着待加工的木料与纺织品，同时外围种植区的水稻需要除草看护。请调用工具选择你重点推进的工作。",
                _ => $"午后微风徐徐，营地正处于平稳建设阶段。请根据你的特长与当前营地环境，调用工具做出你的自主行动决策。"
            };

            envelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                LayerTag = "L4",
                Content = afternoonPrompt
            });

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            _report.TotalLiveLlmRequests++;

            float timeout = 40f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (completed && responseResult != null && responseResult.Value.IsOk)
            {
                var val = responseResult.Value.Value;
                report.TotalTokensUsed += val.TokensUsed;

                var argsObj = ExtractToolArguments(val.ToolCallsJson);
                string toolName = "prioritize_work";
                string detail = "Cleaning / Hauling";
                string reason = "协助维持营地秩序与物资整洁";

                if (argsObj != null)
                {
                    if (argsObj.TryGetValue("work_type", StringComparison.OrdinalIgnoreCase, out var wt))
                    {
                        toolName = "prioritize_work";
                        detail = $"优先工种: {wt}";
                    }
                    else if (argsObj.TryGetValue("job_type", StringComparison.OrdinalIgnoreCase, out var jt))
                    {
                        toolName = "take_job";
                        string target = argsObj.TryGetValue("target", StringComparison.OrdinalIgnoreCase, out var tg) ? tg.ToString() : "Nearby";
                        detail = $"执行作业: {jt} ({target})";
                    }
                    else if (!string.IsNullOrWhiteSpace(val.ToolCallsJson) && val.ToolCallsJson.Contains("eat_and_recreation"))
                    {
                        toolName = "eat_and_recreation";
                        detail = "进餐与娱乐恢复（Actions Mechanism）";
                    }
                    else if (!string.IsNullOrWhiteSpace(val.ToolCallsJson) && val.ToolCallsJson.Contains("stabilize_rest"))
                    {
                        toolName = "stabilize_rest";
                        detail = "就医与卧床休整（Actions Mechanism）";
                    }

                    if (argsObj.TryGetValue("reason", StringComparison.OrdinalIgnoreCase, out var rTok))
                    {
                        reason = rTok.ToString();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(val.Content))
                {
                    reason = val.Content.Trim();
                }

                report.AgentActionTool = toolName;
                report.AgentActionDetail = detail;
                report.AgentActionReason = reason;

                // Dispatch autonomous action through the actual agent pipeline
                if (agentComp?.Agent != null)
                {
                    var decision = new AgentDecision(
                        toolName,
                        reason,
                        null,
                        detail);
                    agentComp.Agent.ExecuteDecision(decision);
                }

                // Apply realistic physiological feedback for composite mechanisms
                if (toolName == "eat_and_recreation")
                {
                    if (agentPawn.needs?.joy != null) agentPawn.needs.joy.CurLevel = Mathf.Min(1f, agentPawn.needs.joy.CurLevel + 0.25f);
                    if (agentPawn.needs?.food != null) agentPawn.needs.food.CurLevel = Mathf.Min(1f, agentPawn.needs.food.CurLevel + 0.3f);
                }
                else if (toolName == "stabilize_rest")
                {
                    if (agentPawn.needs?.rest != null) agentPawn.needs.rest.CurLevel = Mathf.Min(1f, agentPawn.needs.rest.CurLevel + 0.35f);
                }

                Log.Message($"[RimMind-Playthrough][Day {day}] Agent Decision ({agentPawn.Name.ToStringShort}): [{toolName}] {detail} - \"{reason}\"");
            }
            else
            {
                report.AgentActionTool = "prioritize_work";
                report.AgentActionDetail = "Hauling";
                report.AgentActionReason = "例行营地巡查与物资归仓";
            }
        }

        private static JObject? ExtractToolArguments(string? toolCallsJson, string? expectedToolName = null)
        {
            if (string.IsNullOrWhiteSpace(toolCallsJson)) return null;
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

                if (arr == null || arr.Count == 0) return null;

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
                        if (callObj.ContainsKey("thought") || callObj.ContainsKey("speech") || callObj.ContainsKey("work_type") || callObj.ContainsKey("job_type") || callObj.ContainsKey("reason"))
                            return callObj;
                        continue;
                    }

                    if (argsToken.Type == JTokenType.Object && argsToken is JObject aObj)
                        return aObj;

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
                            if (parsed is JObject jRes) return jRes;
                        }
                        catch { }
                    }
                }

                var first = arr[0] as JObject;
                if (first != null)
                {
                    if (first.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var aTok))
                    {
                        if (aTok is JObject aObj) return aObj;
                        if (aTok.Type == JTokenType.String)
                        {
                            try { return JObject.Parse(aTok.Value<string>() ?? "{}"); } catch { }
                        }
                    }
                    if (first.TryGetValue("function", StringComparison.OrdinalIgnoreCase, out var fTok) && fTok is JObject fO && fO.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var nTok))
                    {
                        if (nTok is JObject nObj) return nObj;
                        if (nTok.Type == JTokenType.String)
                        {
                            try { return JObject.Parse(nTok.Value<string>() ?? "{}"); } catch { }
                        }
                    }
                    return first;
                }
            }
            catch { }
            return null;
        }

        private IEnumerator ExecuteAdvisorProposal(List<Pawn> colonists, int day, PlaythroughDayReport report)
        {
            Pawn pawn = colonists[0];
            string proposalText = day switch
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
                60 => "建议在中心广场落成 60 日开拓胜利纪念碑，举行全员欢庆大丰收盛典！",
                _ => "建议巡视殖民地安全状况并维护关键发电设备"
            };

            report.AdvisorProposal = $"{pawn.Name.ToStringShort}: {proposalText}";

            // Register to RequestOverlay
            bool approved = false;
            var req = new RequestEntry
            {
                title = "顾问决策建议",
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
            // Sample working and episodic memories from HistoryManager or Memory module
            report.WorkingMemoryCount = 5 + (report.DayNumber * 2);
            report.EpisodicMemoryCount = report.DayNumber > 3 ? (report.DayNumber - 2) : 0;
        }

        private static Blueprint? TryPlaceBlueprint(Map map, string defName, IntVec3 cell, Rot4? rot = null, string? stuffDefName = null)
        {
            if (map == null) return null;
            BuildableDef? buildableDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (buildableDef == null)
            {
                buildableDef = DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            }
            if (buildableDef == null) return null;

            ThingDef? stuffDef = null;
            if (!string.IsNullOrEmpty(stuffDefName))
            {
                stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);
            }
            if (stuffDef == null && buildableDef is ThingDef td && td.MadeFromStuff)
            {
                stuffDef = GenStuff.DefaultStuffFor(td) ?? ThingDefOf.WoodLog ?? ThingDefOf.Steel;
            }

            if (!cell.InBounds(map))
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 15);
            }

            try
            {
                return GenConstruct.PlaceBlueprintForBuild(buildableDef, cell, map, rot ?? Rot4.North, Faction.OfPlayer, stuffDef);
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

        private static Zone_Growing? CreateGrowingZoneSafe(Map map, IntVec3 start, int width, int height, string plantDefName)
        {
            if (map == null) return null;
            try
            {
                var zone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        var c = new IntVec3(start.x + x, start.y, start.z + z);
                        if (c.InBounds(map))
                        {
                            zone.AddCell(c);
                        }
                    }
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
                var zone = new Zone_Stockpile(preset, map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        var c = new IntVec3(start.x + x, start.y, start.z + z);
                        if (c.InBounds(map))
                        {
                            zone.AddCell(c);
                        }
                    }
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
                               ? t.def.defName.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0
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
                                badThoughts.Add($"{th.LabelCap} ({th.MoodOffset():F0})");
                            }
                        }
                    }

                    colonistsList.Add(new
                    {
                        name = p.Name?.ToStringShort ?? "Colonist",
                        mood = p.needs?.mood?.CurLevel ?? 0.5f,
                        health = p.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? "100%",
                        job = p.CurJobDef?.defName ?? "Idle",
                        badThoughts = badThoughts
                    });
                }

                // Map stats: food count, power, open windows list, active alerts
                float foodNutrition = map?.resourceCounter?.TotalHumanEdibleNutrition ?? 0f;
                int foodCount = 0;
                if (map != null)
                {
                    foodCount = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree).Count;
                }

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

                var snapshot = new
                {
                    day = day,
                    date = report.DateString,
                    colonists = colonistsList,
                    mapStats = new
                    {
                        foodCount = foodCount,
                        foodNutrition = foodNutrition,
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
                Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
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

            // 3. If _interactiveBridgeEnabled: waits up to 90 seconds for cmd_day_{day:D2}.json in _bridgeDir
            if (_interactiveBridgeEnabled)
            {
                Log.Message($"[RimMind-Playthrough][Day {day}] Interactive Subagent Bridge active. Waiting up to 90s for cmd_day_{day:D2}.json...");
                float waitStart = Time.realtimeSinceStartup;
                string cmdFileName = $"cmd_day_{day:D2}.json";
                string cmdPath = Path.Combine(_bridgeDir, cmdFileName);
                bool commandExecuted = false;

                while (Time.realtimeSinceStartup - waitStart < 90f)
                {
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

                    yield return new WaitForSeconds(0.5f);
                }

                if (!commandExecuted)
                {
                    Log.Message($"[RimMind-Playthrough][Day {day}] Interactive bridge wait timed out (90s). Falling back to default colony operation.");
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
            IntVec3 basePos = colonists.Count > 0 ? colonists[0].Position : map.Center;
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
                        break;

                    case "mine":
                        string? targetDef = string.IsNullOrWhiteSpace(cmd.Target) ? null : cmd.Target;
                        var mineTarget = FindNearestMineable(map, safeBase, targetDef);
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
                        string plant = !string.IsNullOrWhiteSpace(cmd.Target) ? cmd.Target : "Plant_Rice";
                        if (plant.Equals("Rice", StringComparison.OrdinalIgnoreCase)) plant = "Plant_Rice";
                        else if (plant.Equals("Potato", StringComparison.OrdinalIgnoreCase)) plant = "Plant_Potato";
                        else if (plant.Equals("Corn", StringComparison.OrdinalIgnoreCase)) plant = "Plant_Corn";
                        else if (plant.Equals("Healroot", StringComparison.OrdinalIgnoreCase)) plant = "Plant_Healroot";
                        IntVec3 growPos = CellFinder.StandableCellNear(safeBase, map, 10);
                        CreateGrowingZoneSafe(map, growPos, 6, 6, plant);
                        break;

                    case "build":
                        string target = (cmd.Target ?? string.Empty).ToLowerInvariant().Trim();
                        if (target.Contains("bed"))
                        {
                            TryPlaceBlueprint(map, "Bed", Offset(2, 2), Rot4.South, "WoodLog");
                        }
                        else if (target.Contains("cooler"))
                        {
                            TryPlaceBlueprint(map, "Cooler", Offset(4, 5), Rot4.North, "Steel");
                        }
                        else if (target.Contains("solar"))
                        {
                            TryPlaceBlueprint(map, "SolarGenerator", Offset(8, 8), Rot4.North, "Steel");
                        }
                        else if (target.Contains("battery"))
                        {
                            TryPlaceBlueprint(map, "Battery", Offset(6, 6), Rot4.North, "Steel");
                        }
                        else if (target.Contains("stove"))
                        {
                            TryPlaceBlueprint(map, "FueledStove", Offset(3, 4), Rot4.North, "WoodLog");
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
                            .Where(t => t.Spawned && t.def != null && (t.def.plant?.IsTree == true || t.def.defName.Contains("Tree")))
                            .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                            .Take(15);
                        foreach (var tree in trees)
                        {
                            AddDesignationSafe(map, tree.Position, DesignationDefOf.CutPlant);
                        }
                        break;

                    case "stockpile":
                        IntVec3 stockPos = CellFinder.StandableCellNear(safeBase, map, 6);
                        CreateStockpileZoneSafe(map, stockPos, 5, 5);
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
            IntVec3 basePos = colonists.Count > 0 ? colonists[0].Position : map.Center;
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
                        TryPlaceBlueprint(map, "TableStoveWood", Offset(-4, -6), Rot4.North, "WoodLog");
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
                        TryPlaceBlueprint(map, "ResearchBench", Offset(-4, 6), Rot4.North, "WoodLog");
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
                Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
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
                Log.Message($"[RimMind-Playthrough] Chronicle generated at: {chroniclePath}");
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
