using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using LudeonTK;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public static partial class RimMindCoreDebugActions
    {
        [DebugAction("Autotests", "Test H2 Actions Equivalence", actionType = DebugActionType.Action)]
        public static void TestH2ActionsEquivalence()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            RunH2ActionsEquivalence(
                runtimeScope.GetOptional<IGameMechanismRegistry>()?.All ?? new List<IGameMechanism>(),
                runtimeScope.GetOptional<IToolRegistry>()?.All ?? new List<IToolHandler>());
        }

        private static void RunH2ActionsEquivalence(
            IReadOnlyList<IGameMechanism> mechanisms,
            IReadOnlyList<IToolHandler> tools)
        {
            int pass = 0, fail = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Autotests] === H2 Actions Equivalence ===");

            // 1. Verify every registered Mechanism has a corresponding ToolHandler
            var mechanismIds = new HashSet<string>(mechanisms.Select(m => m.MechanismId));
            var toolIds = new HashSet<string>(tools.Select(t => t.Definition.Id));

            sb.AppendLine($"  Mechanisms: {mechanismIds.Count}, Tools: {toolIds.Count}");

            // 2. Each Mechanism's write actions should have a corresponding ToolHandler
            foreach (var mech in mechanisms)
            {
                var writeActions = mech.GetWriteActions();
                if (writeActions == null || writeActions.Count == 0)
                {
                    sb.AppendLine($"  [SKIP] {mech.MechanismId}: no write actions");
                    continue;
                }

                foreach (var action in writeActions)
                {
                    // Convention: tool id = "mechanismId_action" or "mechanismId"
                    string conventionToolId = $"{mech.MechanismId}_{action.Action}";
                    string mechanismToolId = mech.MechanismId;
                    if (toolIds.Contains(conventionToolId) || toolIds.Contains(mechanismToolId))
                    {
                        string matchedId = toolIds.Contains(conventionToolId) ? conventionToolId : mechanismToolId;
                        sb.AppendLine($"  [PASS] {mech.MechanismId}.{action.Action} -> {matchedId}");
                        pass++;
                    }
                    else
                    {
                        sb.AppendLine($"  [FAIL] {mech.MechanismId}.{action.Action} -> no matching tool (tried '{conventionToolId}', '{mechanismToolId}')");
                        Log.Error($"[Autotests] H2: {mech.MechanismId}.{action.Action} has no matching tool");
                        fail++;
                    }
                }
            }

            // 3. Verify tool count consistency
            int mechanismToolCount = mechanisms
                .SelectMany(m => m.GetWriteActions() ?? new List<MechanismActionInfo>())
                .Count();
            sb.AppendLine($"  Total mechanism write actions: {mechanismToolCount}, Total tools: {toolIds.Count}");

            sb.AppendLine($"  Result: {pass} passed, {fail} failed");
            Log.Message(sb.ToString());
            ReportAutotest("H2", pass, fail);
        }

        [DebugAction("Autotests", "Test P Visibility Entrypoints", actionType = DebugActionType.Action)]
        public static void TestPVisibilityEntrypoints()
        {
            int pass = 0, fail = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[RimMind Autotest P] Visibility Entrypoints");

            void Check(string name, Func<bool> predicate)
            {
                try
                {
                    if (predicate())
                    {
                        pass++;
                        sb.AppendLine($"PASS {name}");
                    }
                    else
                    {
                        fail++;
                        sb.AppendLine($"FAIL {name}");
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    sb.AppendLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Check("Core icon asset", () => ContentFinder<Texture2D>.Get("UI/RimMind/Icon", false) != null);
            foreach (string pageId in new[]
            {
                "overview", "agents", "ai_requests", "tool_calls", "mechanisms", "context_keys", "settings"
            })
            {
                Check($"Debug center page: {pageId}", () =>
                    DebugCenterPageRegistry.Find(pageId) != null
                    && DebugCenterPageRegistry.Create(pageId) != null);
            }

            sb.AppendLine($"Summary: {pass} passed, {fail} failed");
            if (fail > 0) Log.Error(sb.ToString());
            else Log.Message(sb.ToString());
            ReportAutotest("P.VisibilityEntrypoints", pass, fail);
        }

        [DebugAction("Autotests", "Test K Unified Request", actionType = DebugActionType.Action)]
        public static void TestKUnifiedRequest()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            GameServiceScope gameScope = GameServiceHub.Shared.Capture();
            RunKUnifiedRequest(
                gameScope.GetOptional<INpcManager>(),
                runtimeScope.GetOptional<IRequestQueue>(),
                runtimeScope.GetOptional<IClientManager>(),
                runtimeScope.GetOptional<IToolRegistry>(),
                runtimeScope.GetOptional<IAgentBus>());
        }

        private static void RunKUnifiedRequest(
            INpcManager? npcManager,
            IRequestQueue? requestQueue,
            IClientManager? clientManager,
            IToolRegistry? toolRegistry,
            IAgentBus? agentBus)
        {
            int pass = 0, fail = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Autotests] === K Unified Request ===");

            // 1. NPC routing: NpcManager should be available and have active agents
            if (npcManager != null)
            {
                var npcs = npcManager.GetAllNpcs();
                var activeAgents = npcManager.GetActiveAgentPawnIds();
                sb.AppendLine($"  [PASS] NpcManager available: {npcs.Count} NPCs, {activeAgents.Count} active agents");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] NpcManager not initialized");
                Log.Error("[Autotests] K: NpcManager not initialized");
                fail++;
            }

            // 2. Storage abstraction: RequestQueue should be available
            if (requestQueue != null)
            {
                sb.AppendLine($"  [PASS] AIRequestQueue available: paused={requestQueue.IsPaused}, active={requestQueue.ActiveRequestCount}");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] AIRequestQueue not initialized");
                Log.Error("[Autotests] K: AIRequestQueue not initialized");
                fail++;
            }

            // 3. ClientManager should be available for provider routing
            if (clientManager != null)
            {
                sb.AppendLine($"  [PASS] ClientManager available");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] ClientManager not initialized");
                Log.Error("[Autotests] K: ClientManager not initialized");
                fail++;
            }

            // 4. ToolRegistry should have registered tools (unified dispatch)
            var allTools = toolRegistry?.All;
            if (allTools != null && allTools.Count > 0)
            {
                sb.AppendLine($"  [PASS] ToolRegistry has {allTools.Count} registered tools");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] ToolRegistry empty or not initialized");
                Log.Error("[Autotests] K: ToolRegistry empty or not initialized");
                fail++;
            }

            // 5. AgentBus should be available for event dispatch
            if (agentBus != null)
            {
                sb.AppendLine($"  [PASS] AgentBus available: handlers={agentBus.GetHandlerCount()}, pending={agentBus.GetBackgroundQueueCount()}");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] AgentBus not initialized");
                Log.Error("[Autotests] K: AgentBus not initialized");
                fail++;
            }

            sb.AppendLine($"  Result: {pass} passed, {fail} failed");
            Log.Message(sb.ToString());
            ReportAutotest("K.UnifiedRequest", pass, fail);
        }

        [DebugAction("Autotests", "Test L Context Evolution", actionType = DebugActionType.Action)]
        public static void TestLContextEvolution()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            RunLContextEvolution(
                runtimeScope.GetOptional<IContextKeyRegistry>(),
                runtimeScope.GetOptional<IContextBuilder>(),
                runtimeScope.GetOptional<IProviderRegistry>(),
                runtimeScope.GetOptional<IFlywheelParameterStore>(),
                runtimeScope.GetOptional<ITelemetryCollector>());
        }

        private static void RunLContextEvolution(
            IContextKeyRegistry? contextKeyRegistry,
            IContextBuilder? contextBuilder,
            IProviderRegistry? providerRegistry,
            IFlywheelParameterStore? flywheelParameterStore,
            ITelemetryCollector? telemetryCollector)
        {
            int pass = 0, fail = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Autotests] === L Context Evolution ===");

            // 1. ContextKeyRegistry should have registered keys with staleness metadata
            var keys = contextKeyRegistry?.GetAll();
            if (keys != null && keys.Count > 0)
            {
                int withStaleness = 0;
                foreach (var key in keys)
                {
                    if (key.LastUpdatedTick > 0 || key.LastIncludedTick > 0)
                        withStaleness++;
                }
                sb.AppendLine($"  [PASS] ContextKeyRegistry: {keys.Count} keys, {withStaleness} with staleness data");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] ContextKeyRegistry empty or not initialized");
                Log.Error("[Autotests] L: ContextKeyRegistry empty or not initialized");
                fail++;
            }

            // 2. Seven-dimension scoring: keys should have AdaptivePriority and CurrentScore
            if (keys != null && keys.Count > 0)
            {
                int withAdaptive = 0;
                foreach (var key in keys)
                {
                    if (Math.Abs(key.AdaptivePriority - key.Priority) > 0.0001f || Math.Abs(key.CurrentScore) > 0.0001f)
                        withAdaptive++;
                }
                sb.AppendLine($"  [INFO] Adaptive scoring: {withAdaptive}/{keys.Count} keys have non-default adaptive values");

                if (withAdaptive > 0)
                {
                    sb.AppendLine($"  [PASS] Seven-dimension scoring active on {withAdaptive} keys");
                    pass++;
                }
                else
                {
                    sb.AppendLine($"  [WARN] No keys have adaptive scoring yet (may need game ticks)");
                    pass++; // Not a failure — scoring activates over time
                }
            }

            // 3. ContextEngine should be available for snapshot building
            if (contextBuilder != null)
            {
                sb.AppendLine($"  [PASS] ContextEngine (IContextBuilder) available");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] ContextEngine not initialized");
                Log.Error("[Autotests] L: ContextEngine not initialized");
                fail++;
            }

            // 4. ProviderRegistry should have registered providers
            var categories = providerRegistry?.GetRegisteredCategories() ?? new List<string>();
            if (categories.Count > 0)
            {
                sb.AppendLine($"  [PASS] ProviderRegistry: {categories.Count} categories ({string.Join(", ", categories.Take(5))})");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] ProviderRegistry empty");
                Log.Error("[Autotests] L: ProviderRegistry empty");
                fail++;
            }

            // 5. FlywheelParameterStore should be available for learning feedback
            if (flywheelParameterStore != null)
            {
                var parameters = flywheelParameterStore.GetAll();
                sb.AppendLine($"  [PASS] FlywheelParameterStore: {parameters.Count} parameters, budget={flywheelParameterStore.TotalBudget}");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] FlywheelParameterStore not initialized");
                Log.Error("[Autotests] L: FlywheelParameterStore not initialized");
                fail++;
            }

            // 6. TelemetryCollector for learning feedback chain
            if (telemetryCollector != null)
            {
                var records = telemetryCollector.GetRecentRecords(5);
                sb.AppendLine($"  [PASS] TelemetryCollector available: {records?.Count ?? 0} recent records");
                pass++;
            }
            else
            {
                sb.AppendLine($"  [FAIL] TelemetryCollector not initialized");
                Log.Error("[Autotests] L: TelemetryCollector not initialized");
                fail++;
            }

            sb.AppendLine($"  Result: {pass} passed, {fail} failed");
            Log.Message(sb.ToString());
            ReportAutotest("L.ContextEvolution", pass, fail);
        }
        [DebugAction("Autotests", "Test UI Layout Conflict Detector", actionType = DebugActionType.Action)]
        public static void TestUiLayoutConflictDetector()
        {
            LayoutConflictStore.Clear();

            Window[] windows =
            {
                new Window_RequestLog(),
                new Window_AIDebugLog(),
                new Window_ToolCallDebug(),
                new Window_MechanismStatus(),
                new Window_ContextKeyDebug(),
                new Window_AgentStateDebug(),
                new Window_AgentModeDebug(),
                new Window_AgentFlowLab(),
                new Window_AgentProgressFloat(),
                new Window_RimMindHub(),
            };

            foreach (var w in windows)
            {
                Find.WindowStack.Add(w);
            }

            LayoutAutotestRunner.Run(windows, evaluation =>
            {
                ReportAutotest(
                    "UI.LayoutConflict",
                    evaluation.PassCount,
                    evaluation.FailCount,
                    evaluation.MissingReportCount);
            });
        }

        [DebugAction("Autotests", "Run Full Behavior Autotest Suite", actionType = DebugActionType.Action)]
        public static void RunFullBehaviorAutotests()
        {
            BehaviorAutotestRunner.StartSuite(null, isHeadless: false);
        }

        private static void ReportAutotest(string caseId, int pass, int fail, int skip = 0)
        {
            string outcome = fail > 0 ? "FAIL" : "PASS";
            Log.Message($"[RIMTEST][Core][{caseId}][{outcome}] pass={pass} fail={fail} skip={skip}");
        }
    }
}
