using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;
using LudeonTK;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public static partial class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Show Map Context", actionType = DebugActionType.Action)]
        public static void ShowMapContext()
        {
            var map = Find.CurrentMap;
            if (map == null) { RimMindErrors.Warn("[RimMind-Core] No map loaded."); return; }
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var contextKeyProvider = runtimeScope.GetOptional<IContextKeyProvider>();
            if (contextKeyProvider == null) { RimMindErrors.Warn("[RimMind-Core] ContextKeyProvider not available."); return; }
            var entries = contextKeyProvider.BuildMapContextEntries(map);
            var sb = new System.Text.StringBuilder();
            foreach (var entry in entries)
                sb.AppendLine(entry.Content);
            Log.Message("[RimMind-Core] Map Context:\n" + sb.ToString().TrimEnd());
        }

        [DebugAction("RimMind", "Show Pawn Context (selected)", actionType = DebugActionType.Action)]
        public static void ShowPawnContext()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null) { RimMindErrors.Warn("[RimMind-Core] Select a pawn first."); return; }
            var npcId = $"NPC-{pawn.thingIDNumber}";
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var contextEngine = runtimeScope.GetOptional<IContextBuilder>();
            if (contextEngine == null) { RimMindErrors.Warn("[RimMind-Core] ContextEngine not available."); return; }
            _ = LogPawnContextAsync(contextEngine, pawn, npcId, runtimeScope.Token);
            Log.Message("[RimMind-Core] Building selected pawn context asynchronously.");
        }

        private static async Task LogPawnContextAsync(
            IContextBuilder contextEngine,
            Pawn pawn,
            string npcId,
            RuntimeGenerationToken token)
        {
            try
            {
                var snapshot = await contextEngine.BuildSnapshotFromEnvelopeAsync(npcId, "[Debug] Show context");
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (!RuntimeServiceHub.Shared.IsCurrent(token))
                    {
                        RuntimeServiceHub.Shared.RecordStaleCompletion(LifecycleEventSources.DebugAction);
                        return;
                    }
                    LogContextSnapshot(pawn, npcId, snapshot);
                });
            }
            catch (Exception ex)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (!RuntimeServiceHub.Shared.IsCurrent(token))
                    {
                        RuntimeServiceHub.Shared.RecordStaleCompletion(LifecycleEventSources.DebugAction);
                        return;
                    }
                    RimMindErrors.Warn($"[RimMind-Core] Context preview failed: {ex.Message}");
                });
            }
        }

        private static void LogContextSnapshot(Pawn pawn, string npcId, ContextSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                RimMindErrors.Warn("[RimMind-Core] Context preview returned no snapshot.");
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind-Core] Context Snapshot for {pawn.Name?.ToStringShort} (NpcId={npcId}):");
            sb.AppendLine($"Estimated tokens: {snapshot.EstimatedTokens}");
            sb.AppendLine($"L0={snapshot.Meta.L0Tokens} L1={snapshot.Meta.L1Tokens} L2={snapshot.Meta.L2Tokens} L3={snapshot.Meta.L3Tokens} L4={snapshot.Meta.L4Tokens}");
            sb.AppendLine("=== Messages ===");
            foreach (var msg in snapshot.Messages)
                sb.AppendLine($"[{msg.Role}] {msg.Content}");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Registered Providers", actionType = DebugActionType.Action)]
        public static void ShowRegisteredProviders()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var providerRegistry = runtimeScope.GetOptional<IProviderRegistry>();
            var categories = providerRegistry?.GetRegisteredCategories() ?? new List<string>();
            if (categories.Count == 0)
            {
                Log.Message("[RimMind-Core] No registered providers.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind-Core] Registered Providers ({categories.Count}):");

            Pawn? firstColonist = Enumerable.FirstOrDefault(
                Find.CurrentMap?.mapPawns?.FreeColonists ?? new System.Collections.Generic.List<Pawn>());

            foreach (var cat in categories)
            {
                sb.AppendLine($"  [{cat}]");

                if (providerRegistry != null)
                {
                    var staticData = providerRegistry.GetStaticProviderData(cat);
                    if (staticData.IsOk && staticData.Value != null)
                        sb.AppendLine($"    Static: {staticData.Value.Length} chars");
                    else if (staticData.IsErr)
                        sb.AppendLine($"    Static: ERROR - {staticData.Error.Message}");

                    if (firstColonist != null)
                    {
                        var pawnData = providerRegistry.GetProviderData(cat, firstColonist);
                        if (pawnData.IsOk && pawnData.Value != null)
                            sb.AppendLine($"    Pawn ({firstColonist.Name?.ToStringShort}): {pawnData.Value.Length} chars");
                        else if (pawnData.IsErr)
                            sb.AppendLine($"    Pawn: ERROR - {pawnData.Error.Message}");
                    }
                }
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Registered ContextKeys", actionType = DebugActionType.Action)]
        public static void ShowRegisteredContextKeys()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var keys = runtimeScope.GetOptional<IContextKeyRegistry>()?.GetAll();
            if (keys.Count == 0)
            {
                Log.Message("[RimMind-Core] No registered context keys.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind-Core] Registered ContextKeys ({keys.Count}):");

            foreach (var key in keys)
            {
                sb.AppendLine($"  {key.Key} | Layer={key.Layer} | Priority={key.GetEffectivePriority():F3} | OwnerMod={key.OwnerMod} | Updates={key.UpdateCount}");
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show Flywheel State", actionType = DebugActionType.Action)]
        public static void ShowFlywheelState()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var flywheelParameterStore = runtimeScope.GetOptional<IFlywheelParameterStore>();
            if (flywheelParameterStore == null)
            {
                RimMindErrors.Warn("[RimMind-Core] FlywheelParameterStore not initialized.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind-Core] === Flywheel State ===");

            var current = flywheelParameterStore.GetAll();
            var defaults = flywheelParameterStore.GetDefaults();

            sb.AppendLine("  Parameters:");
            foreach (var kvp in current)
            {
                string defaultTag = defaults.TryGetValue(kvp.Key, out var def) && Math.Abs(def - kvp.Value) > 0.0001f
                    ? $" (default={def})"
                    : "";
                sb.AppendLine($"    {kvp.Key} = {kvp.Value}{defaultTag}");
            }

            sb.AppendLine($"  TotalBudget: {flywheelParameterStore.TotalBudget}");

            var recentRecords = runtimeScope.GetOptional<ITelemetryCollector>()?.GetRecentRecords(RimMindDefaults.TelemetryRecordLimit);
            sb.AppendLine($"  Telemetry records (recent {RimMindDefaults.TelemetryRecordLimit}): {recentRecords?.Count ?? 0}");

            Log.Message(sb.ToString());
        }
        [DebugAction("RimMind", "Show AgentBus Subscribers", actionType = DebugActionType.Action)]
        public static void ShowAgentBusSubscribers()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RimMind-Core] === AgentBus Subscribers ===");

            var agentBus = runtimeScope.GetOptional<IAgentBus>();
            sb.AppendLine($"  AgentBus type: {agentBus?.GetType().Name ?? "null"}");

            sb.AppendLine($"  Registered event types: {agentBus?.GetHandlerCount() ?? 0}");

            sb.AppendLine($"  Background queue pending: {agentBus?.GetBackgroundQueueCount() ?? 0}");

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show History State (selected)", actionType = DebugActionType.Action)]
        public static void ShowHistoryState()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                RimMindErrors.Warn("[RimMind-Core] Select a pawn first.");
                return;
            }

            var npcId = $"NPC-{pawn.thingIDNumber}";
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var historyManager = runtimeScope.GetOptional<IHistoryManager>();
            var count = historyManager?.GetHistoryCount(npcId) ?? 0;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind-Core] History State for {pawn.Name?.ToStringShort} (NpcId={npcId}):");
            sb.AppendLine($"  Total entries: {count}");

            if (count > 0 && historyManager != null)
            {
                var recent = historyManager.GetHistory(npcId, 3);
                sb.AppendLine($"  Last {recent.Count} entries:");
                foreach (var (role, content) in recent)
                {
                    string preview = content.Length > RimMindDefaults.PreviewTruncateLength ? content.Substring(0, RimMindDefaults.PreviewTruncateLength) + "..." : content;
                    sb.AppendLine($"    [{role}] {preview}");
                }
            }

            if (historyManager != null)
            {
                var allForSave = historyManager.GetAllForSaveDict();
                sb.AppendLine($"  Total NPC histories: {allForSave.Count}");
            }

            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Show NPC Manager State", actionType = DebugActionType.Action)]
        public static void ShowNpcManagerState()
        {
            GameServiceScope gameScope = GameServiceHub.Shared.Capture();
            var npcManager = gameScope.GetOptional<INpcManager>();
            if (npcManager == null)
            {
                RimMindErrors.Warn("[RimMind-Core] NpcManager not initialized.");
                return;
            }

            var npcs = npcManager.GetAllNpcs();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RimMind-Core] NPC Manager State:");
            sb.AppendLine($"  Total NPCs: {npcs.Count}");

            foreach (var npc in npcs)
            {
                sb.AppendLine($"  [{npc.NpcId}] Name={npc.Name} Commands={npc.Commands.Count}");
                if (!string.IsNullOrEmpty(npc.CharacterDescription))
                {
                    string desc = npc.CharacterDescription.Length > RimMindDefaults.DescriptionTruncateLength
                        ? npc.CharacterDescription.Substring(0, RimMindDefaults.DescriptionTruncateLength) + "..."
                        : npc.CharacterDescription;
                    sb.AppendLine($"    Desc: {desc}");
                }
            }

            Log.Message(sb.ToString());
        }
    }
}
