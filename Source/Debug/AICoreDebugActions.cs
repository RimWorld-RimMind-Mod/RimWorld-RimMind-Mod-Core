using System;
using System.Linq;
using RimMind.Core.Agent;
using RimMind.Core.AgentBus;
using RimMind.Core.Client;
using RimMind.Core.Context;
using RimMind.Core.Flywheel;
using RimMind.Core.Internal;
using RimMind.Core.Npc;
using RimMind.Core.Settings;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimMind.Core.Debug
{
    [StaticConstructorOnStartup]
    public static class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Test API Connection", actionType = DebugActionType.Action)]
        public static void TestConnection()
        {
            if (!RimMindAPI.IsConfigured())
            {
                Log.Warning("[RimMind] API not configured. Set API Key in mod settings.");
                return;
            }

            var request = new AIRequest
            {
                SystemPrompt = "You are a test assistant. Always reply in JSON format.",
                UserPrompt = "Reply with: {\"status\":\"ok\",\"message\":\"RimMind works\"}",
                MaxTokens = 60,
                Temperature = 0f,
                RequestId = "Debug_TestConnection",
                ModId = "Debug",
                ExpireAtTicks = Find.TickManager.TicksGame + 3600,
                Priority = AIRequestPriority.High,
            };

            RimMindAPI.RequestImmediate(request, response =>
            {
                if (response.Success)
                    Messages.Message("RimMind.Core.Debug.ConnectionSuccess".Translate(response.Content), MessageTypeDefOf.PositiveEvent, false);
                else
                    Messages.Message("RimMind.Core.Debug.ConnectionFailed".Translate(response.Error), MessageTypeDefOf.NegativeEvent, false);
            });

            Messages.Message("RimMind.Core.Debug.RequestSent".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("RimMind", "Show Last Prompt", actionType = DebugActionType.Action)]
        public static void ShowLastPrompt()
        {
            var entries = AIDebugLog.Instance?.Entries;
            if (entries == null || entries.Count == 0)
            {
                Log.Message("[RimMind] No request records.");
                return;
            }
            var last = entries[entries.Count - 1];
            Log.Message($"[RimMind] Last request ({last.Source}):\n" +
                        $"=== System Prompt ===\n{last.FullSystemPrompt}\n" +
                        $"=== User Prompt ===\n{last.FullUserPrompt}\n" +
                        $"=== Response ===\n{last.FullResponse}");
        }

        [DebugAction("RimMind", "Clear Debug Log", actionType = DebugActionType.Action)]
        public static void ClearLog()
        {
            AIDebugLog.Instance?.Clear();
            Log.Message("[RimMind] Debug log cleared.");
        }

        [DebugAction("RimMind", "Clear All Cooldowns", actionType = DebugActionType.Action)]
        public static void ClearCooldowns()
        {
            AIRequestQueue.Instance?.ClearAllCooldowns();
            Log.Message("[RimMind] All cooldowns cleared.");
        }

        [DebugAction("RimMind", "Show Map Context", actionType = DebugActionType.Action)]
        public static void ShowMapContext()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Warning("[RimMind] No map loaded."); return; }
            Log.Message("[RimMind] Map Context:\n" + RimMindAPI.BuildMapContext(map));
        }

        [DebugAction("RimMind", "Show Pawn Context (selected)", actionType = DebugActionType.Action)]
        public static void ShowPawnContext()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null) { Log.Warning("[RimMind] Select a pawn first."); return; }
            var npcId = $"NPC-{pawn.thingIDNumber}";
            var request = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Dialogue,
                Budget = 0.6f,
                CurrentQuery = "[Debug] Show context",
            };
            var engine = RimMindAPI.GetContextEngine();
            var snapshot = engine.BuildSnapshot(request);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] Context Snapshot for {pawn.Name?.ToStringShort} (NpcId={npcId}):");
            sb.AppendLine($"Estimated tokens: {snapshot.EstimatedTokens}");
            sb.AppendLine($"L0={snapshot.Meta.L0Tokens} L1={snapshot.Meta.L1Tokens} L2={snapshot.Meta.L2Tokens} L3={snapshot.Meta.L3Tokens} L4={snapshot.Meta.L4Tokens}");
            sb.AppendLine("=== Messages ===");
            foreach (var msg in snapshot.Messages)
                sb.AppendLine($"[{msg.Role}] {msg.Content}");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Queue State", actionType = DebugActionType.Action)]
        public static void ShowQueueState()
        {
            var queue = AIRequestQueue.Instance;
            if (queue == null)
            {
                Log.Warning("[RimMind] AIRequestQueue not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind] === Queue State ===");
            sb.AppendLine($"  Paused: {queue.IsPaused}");
            sb.AppendLine($"  Active requests: {queue.ActiveRequestCount}");
            sb.AppendLine($"  Local model busy: {queue.IsLocalModelBusy}");

            var active = queue.GetActiveRequests();
            foreach (var t in active)
            {
                sb.AppendLine($"  [Active] {t.Request.RequestId} mod={t.Request.ModId} " +
                              $"priority={t.Request.Priority} state={t.State} attempt={t.AttemptCount}");
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
            RimMindAPI.PauseQueue();
            Log.Message("[RimMind] Queue paused.");
        }

        [DebugAction("RimMind", "Resume Queue", actionType = DebugActionType.Action)]
        public static void ResumeQueue()
        {
            RimMindAPI.ResumeQueue();
            Log.Message("[RimMind] Queue resumed.");
        }

        [DebugAction("RimMind", "Show Registered Providers", actionType = DebugActionType.Action)]
        public static void ShowRegisteredProviders()
        {
            var categories = RimMindAPI.GetRegisteredCategories();
            if (categories.Count == 0)
            {
                Log.Message("[RimMind] No registered providers.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] Registered Providers ({categories.Count}):");

            Pawn? firstColonist = Enumerable.FirstOrDefault(
                Find.CurrentMap?.mapPawns?.FreeColonists ?? new System.Collections.Generic.List<Pawn>());

            foreach (var cat in categories)
            {
                sb.AppendLine($"  [{cat}]");

                var staticData = RimMindAPI.GetStaticProviderData(cat);
                if (staticData != null)
                    sb.AppendLine($"    Static: {staticData.Length} chars");

                if (firstColonist != null)
                {
                    var pawnData = RimMindAPI.GetProviderData(cat, firstColonist);
                    if (pawnData != null)
                        sb.AppendLine($"    Pawn ({firstColonist.Name?.ToStringShort}): {pawnData.Length} chars");
                }
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Registered ContextKeys", actionType = DebugActionType.Action)]
        public static void ShowRegisteredContextKeys()
        {
            var keys = ContextKeyRegistry.GetAll();
            if (keys.Count == 0)
            {
                Log.Message("[RimMind] No registered context keys.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] Registered ContextKeys ({keys.Count}):");

            foreach (var key in keys)
            {
                sb.AppendLine($"  {key.Key} | Layer={key.Layer} | Priority={key.GetEffectivePriority():F3} | OwnerMod={key.OwnerMod} | Updates={key.UpdateCount}");
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Flywheel State", actionType = DebugActionType.Action)]
        public static void ShowFlywheelState()
        {
            var store = FlywheelParameterStore.Instance;
            if (store == null)
            {
                Log.Warning("[RimMind] FlywheelParameterStore not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind] === Flywheel State ===");

            var current = store.GetAll();
            var defaults = store.GetDefaults();

            sb.AppendLine("  Parameters:");
            foreach (var kvp in current)
            {
                string defaultTag = defaults.TryGetValue(kvp.Key, out var def) && Math.Abs(def - kvp.Value) > 0.0001f
                    ? $" (default={def})"
                    : "";
                sb.AppendLine($"    {kvp.Key} = {kvp.Value}{defaultTag}");
            }

            sb.AppendLine($"  TotalBudget: {store.TotalBudget}");

            var telemetry = RimMindAPI.Telemetry;
            var recentRecords = telemetry.GetRecentRecords(100);
            sb.AppendLine($"  Telemetry records (recent 100): {recentRecords?.Count ?? 0}");

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Agent State (selected)", actionType = DebugActionType.Action)]
        public static void ShowAgentState()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind] Select a pawn first.");
                return;
            }

            var comp = RimMind.Core.Comps.CompPawnAgent.GetComp(pawn);
            if (comp == null || comp.Agent == null)
            {
                Log.Warning($"[RimMind] {pawn.Name?.ToStringShort} has no PawnAgent comp.");
                return;
            }

            var agent = comp.Agent;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] Agent State for {pawn.Name?.ToStringShort}:");
            sb.AppendLine($"  State: {agent.State}");
            sb.AppendLine($"  IsActive: {agent.IsActive}");

            sb.AppendLine($"  Identity:");
            sb.AppendLine($"    Motivations: [{string.Join(", ", agent.Identity.Motivations)}]");
            sb.AppendLine($"    PersonalityTraits: [{string.Join(", ", agent.Identity.PersonalityTraits)}]");
            sb.AppendLine($"    CoreValues: [{string.Join(", ", agent.Identity.CoreValues)}]");

            sb.AppendLine($"  Goals (Total={agent.GoalStack.TotalCount}, Active={agent.GoalStack.ActiveCount}):");
            foreach (var g in agent.GoalStack.Goals)
                sb.AppendLine($"    [{g.Status}] {g.Description} | Cat={g.Category} P={g.Priority:F1} Progress={g.Progress:F2}");

            sb.AppendLine($"  Behavior History: {agent.BehaviorHistory.Count}");

            var topW = agent.StrategyOptimizer.GetTopN(5);
            if (topW.Count > 0)
            {
                sb.AppendLine("  Strategy Weights (Top 5):");
                foreach (var kv in topW)
                    sb.AppendLine($"    {kv.Key}: {kv.Value:F2}");
            }

            sb.AppendLine($"  Perception Buffer: {agent.PerceptionBuffer.Entries.Count} entries");

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show AgentBus Subscribers", actionType = DebugActionType.Action)]
        public static void ShowAgentBusSubscribers()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind] === AgentBus Subscribers ===");

            var eventBus = RimMindAPI.GetEventBus();
            sb.AppendLine($"  EventBus type: {eventBus?.GetType().Name ?? "null"}");

            var handlersField = typeof(global::RimMind.Core.AgentBus.AgentBus).GetField("_handlers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (handlersField != null)
            {
                var handlers = handlersField.GetValue(null) as System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<Delegate>>;
                if (handlers != null)
                {
                    sb.AppendLine($"  Registered event types: {handlers.Count}");
                    foreach (var kvp in handlers)
                    {
                        Delegate[] snapshot;
                        lock (kvp.Value)
                        {
                            snapshot = kvp.Value.ToArray();
                        }
                        sb.AppendLine($"    {kvp.Key.Name}: {snapshot.Length} subscriber(s)");
                        foreach (var d in snapshot)
                        {
                            sb.AppendLine($"      {d.Method.DeclaringType?.Name}.{d.Method.Name}");
                        }
                    }
                }
            }
            else
            {
                sb.AppendLine("  Could not access _handlers via reflection.");
            }

            var bgQueueField = typeof(global::RimMind.Core.AgentBus.AgentBus).GetField("_backgroundQueue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (bgQueueField != null)
            {
                var queue = bgQueueField.GetValue(null) as System.Collections.Concurrent.ConcurrentQueue<AgentBusEvent>;
                sb.AppendLine($"  Background queue pending: {queue?.Count ?? 0}");
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show History State (selected)", actionType = DebugActionType.Action)]
        public static void ShowHistoryState()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind] Select a pawn first.");
                return;
            }

            var npcId = $"NPC-{pawn.thingIDNumber}";
            var history = HistoryManager.Instance;
            var count = history.GetHistoryCount(npcId);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] History State for {pawn.Name?.ToStringShort} (NpcId={npcId}):");
            sb.AppendLine($"  Total entries: {count}");

            if (count > 0)
            {
                var recent = history.GetHistory(npcId, 3);
                sb.AppendLine($"  Last {recent.Count} entries:");
                foreach (var (role, content) in recent)
                {
                    string preview = content.Length > 120 ? content.Substring(0, 120) + "..." : content;
                    sb.AppendLine($"    [{role}] {preview}");
                }
            }

            var allForSave = history.GetAllForSave();
            sb.AppendLine($"  Total NPC histories: {allForSave.Count}");

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show NPC Manager State", actionType = DebugActionType.Action)]
        public static void ShowNpcManagerState()
        {
            var mgr = NpcManager.Instance;
            if (mgr == null)
            {
                Log.Warning("[RimMind] NpcManager not initialized.");
                return;
            }

            var npcs = mgr.GetAllNpcs();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind] NPC Manager State:");
            sb.AppendLine($"  Total NPCs: {npcs.Count}");

            foreach (var npc in npcs)
            {
                sb.AppendLine($"  [{npc.NpcId}] Name={npc.Name} Type={npc.Type} Commands={npc.Commands.Count}");
                if (!string.IsNullOrEmpty(npc.CharacterDescription))
                {
                    string desc = npc.CharacterDescription.Length > 80
                        ? npc.CharacterDescription.Substring(0, 80) + "..."
                        : npc.CharacterDescription;
                    sb.AppendLine($"    Desc: {desc}");
                }
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Settings Summary", actionType = DebugActionType.Action)]
        public static void ShowSettingsSummary()
        {
            var s = RimMindCoreMod.Settings;
            if (s == null)
            {
                Log.Warning("[RimMind] Settings not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind] === Settings Summary ===");
            sb.AppendLine($"  Provider: {s.provider}");
            sb.AppendLine($"  Model: {s.modelName}");
            sb.AppendLine($"  Endpoint: {s.apiEndpoint}");
            sb.AppendLine($"  API Key: {(string.IsNullOrEmpty(s.apiKey) ? "(empty)" : $"({s.apiKey.Length} chars)")}");
            sb.AppendLine($"  ForceJsonMode: {s.forceJsonMode}");
            sb.AppendLine($"  MaxTokens: {s.maxTokens}");
            sb.AppendLine($"  DefaultTemperature: {s.defaultTemperature}");
            sb.AppendLine($"  DebugLogging: {s.debugLogging}");
            sb.AppendLine($"  MaxConcurrentRequests: {s.maxConcurrentRequests}");
            sb.AppendLine($"  MaxRetryCount: {s.maxRetryCount}");
            sb.AppendLine($"  RequestTimeoutMs: {s.requestTimeoutMs}");
            sb.AppendLine($"  AutoApplyMode: {s.autoApplyMode}");
            sb.AppendLine($"  AutoApplyConfidenceThreshold: {s.autoApplyConfidenceThreshold}");
            sb.AppendLine($"  RequestOverlayEnabled: {s.requestOverlayEnabled}");
            sb.AppendLine($"  Player2RemoteUrl: {s.player2RemoteUrl}");
            sb.AppendLine($"  TelemetryDataPath: {(string.IsNullOrEmpty(s.telemetryDataPath) ? "(default)" : s.telemetryDataPath)}");
            sb.AppendLine($"  AnalysisReportPath: {(string.IsNullOrEmpty(s.analysisReportPath) ? "(default)" : s.analysisReportPath)}");
            sb.AppendLine($"  IsConfigured: {s.IsConfigured()}");

            Log.Message(sb.ToString());
        }
    }
}
