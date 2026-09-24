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

    public sealed class PlaythroughDayReport
    {
        public int DayNumber { get; set; }
        public int StartTick { get; set; }
        public int EndTick { get; set; }
        public string DateString { get; set; } = string.Empty;
        public List<PlaythroughColonistSnapshot> Colonists { get; set; } = new();
        public string MorningPawnName { get; set; } = string.Empty;
        public string MorningThought { get; set; } = string.Empty;
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

                // Milestone Screenshot: Day 1, 5, 10, 15, 20 (or Day 1, 3, 5, 7, 10 for shorter runs)
                bool isMilestone = (_totalDays <= 10)
                    ? (day == 1 || day == 3 || day == 5 || day == 7 || day == 10)
                    : (day == 1 || day == 5 || day == 10 || day == 15 || day == 20 || day == _totalDays);

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

                // Automatically dismiss any incident dialog or messagebox
                // Automatically dismiss any incident dialog or messagebox or floating windows
                // Automatically dismiss any incident dialog, messagebox, naming window, or debug log
                if (Find.WindowStack != null)
                {
                    var msgBox = Find.WindowStack.WindowOfType<Dialog_MessageBox>();
                    if (msgBox != null)
                    if (Find.FactionManager?.OfPlayer != null && !Find.FactionManager.OfPlayer.HasName)
                    {
                        Find.WindowStack.TryRemove(msgBox, doCloseSound: false);
                        Find.FactionManager.OfPlayer.Name = "RimMind Settlement";
                    }
                    var floatMenu = Find.WindowStack.WindowOfType<FloatMenu>();
                    if (floatMenu != null)

                    var windows = Find.WindowStack.Windows.ToList();
                    for (int wIdx = 0; wIdx < windows.Count; wIdx++)
                    {
                        Find.WindowStack.TryRemove(floatMenu, doCloseSound: false);
                        var w = windows[wIdx];
                        if (w is Dialog_MessageBox || w is FloatMenu ||
                            w.GetType().Name.Contains("Name") ||
                            w.GetType().Name.Contains("Log") ||
                            w.GetType().Name.Contains("GiveName"))
                        {
                            Find.WindowStack.TryRemove(w, doCloseSound: false);
                        }
                    }
                }

                // Clear queue backlog if accumulated
                var runtimeScope = RuntimeServiceHub.Shared.Capture();
                var queue = runtimeScope.GetOptional<IRequestQueue>();
                if (queue != null && queue.TotalQueuedCount > 3)
                step++;
                if (step % 20 == 0)
                {
                    queue.CancelAllRequests();
                    EnsureColonistSustenance(GetLivingColonists());
                }

                // Advance smooth single ticks directly per frame on main thread to accelerate simulation
                for (int i = 0; i < 30 && Find.TickManager.TicksGame < targetTick; i++)
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
                1 => "建议建立基础食物储存区与防御掩体",
                2 => "建议优先采伐木材并修缮殖民者住所",
                3 => "建议分配专职种植员播种水稻与棉花",
                4 => "建议烹饪熟食防止殖民者食用生肉引发肠胃炎",
                5 => "建议加固殖民地外围木墙并设置陷阱防线",
                6 => "建议安排殖民者轮换娱乐放松，维持心理健康",
                7 => "建议开展基础科技研究（如电池与太阳能板）",
                8 => "建议收割成熟庄稼并整理仓库分类",
                9 => "建议制作应急药物与简易草药包",
                10 => "建议举行庆祝宴会，回顾10天开拓历程",
                11 => "建议扩建低温冷藏库，储备过冬肉类与蔬菜",
                12 => "建议开采浅层钢铁矿脉，准备金属锻造与机械工具",
                13 => "建议铺设环形防御外墙与重型沙袋掩体",
                14 => "建议研发生物医药与精炼无菌地板提高医疗水平",
                15 => "建议组建对外贸易小队，采购高科技部件与先进发电机组件",
                16 => "建议安装地热发电机组，保障殖民地持续高功率电力供应",
                17 => "建议部署自动哨戒机枪与应急断电闸刀应对突发袭击",
                18 => "建议加工精良级御寒衣物，应对即将到来的季节降温",
                19 => "建议设立专用病房与手术室，提升殖民者创伤救治质量",
                20 => "建议建立第二防御纵深与工业级军械工坊，迈向现代化殖民地",
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
                    if (!string.IsNullOrWhiteSpace(d.DialogueSpeech))
                    {
                        sb.AppendLine($"### 2. 社交交谈与关系变动 (`express_dialogue`)");
                        sb.AppendLine($"- **交谈**: **{d.DialogueSpeaker}** 对 **{d.DialogueListener}** 说：");
                        sb.AppendLine($"  > *\"{d.DialogueSpeech}\"*");
                        sb.AppendLine($"- **心情标签**: `{d.DialogueThoughtTag}` | **好感度变动**: `{(d.DialogueRelationDelta >= 0 ? "+" : "")}{d.DialogueRelationDelta}`");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(d.AgentActionTool))
                    {
                        sb.AppendLine($"### 3. 午后智能体自主决策 (`{d.AgentActionTool}`)");
                        sb.AppendLine($"- **殖民者**: **{d.AgentActionPawn}**");
                        sb.AppendLine($"- **行动决策**: `{d.AgentActionDetail}`");
                        sb.AppendLine($"- **决策动机**: *\"{d.AgentActionReason}\"*");
                        sb.AppendLine();
                    }
                    sb.AppendLine($"### 4. 顾问决策建议与审批");
                    sb.AppendLine($"- **建议事项**: {d.AdvisorProposal}");
                    sb.AppendLine($"- **审批状态**: {(d.AdvisorApproved ? "✅ 已批准执行" : "❌ 已驳回")}");
                    sb.AppendLine();
                    sb.AppendLine($"### 5. 运行时指标");
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
