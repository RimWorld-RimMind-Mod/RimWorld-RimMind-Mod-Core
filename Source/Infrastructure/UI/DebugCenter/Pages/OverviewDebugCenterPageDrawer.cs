using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Client;
using RimWorld;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI.DebugCenter.Overview;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class OverviewDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private IAgentLoopScheduler? _agentLoopScheduler;
        private IRequestQueue? _requestQueue;
        private ISettingsProvider? _settings;
        private IClientManager? _clientManager;
        private Vector2 _scrollPosition;
        internal void ScrollToBottom() => _scrollPosition = new Vector2(0, 100000f);

        public IDisposable? Bind(RuntimeServiceScope scope)
        {
            _agentLoopScheduler = scope.GetOptional<IAgentLoopScheduler>();
            _requestQueue = scope.GetOptional<IRequestQueue>();
            _settings = scope.GetOptional<ISettingsProvider>();
            _clientManager = scope.GetOptional<IClientManager>();
            return null;
        }

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            Pawn? selectedPawn = context.SelectedPawn ?? Find.Selector.SingleSelectedThing as Pawn;
            DebugCenterOverviewModel model = BuildModel(selectedPawn);
            DebugCenterOverviewLayoutResult layout = DebugCenterOverviewLayout.Calculate(rect);

            scope.Record(layout.Viewport, "Hub:Overview:ScrollViewport");
            Widgets.BeginScrollView(layout.Viewport, ref _scrollPosition, layout.ViewRect);
            try
            {
                DrawOverviewContent(layout, context, selectedPawn, model);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawOverviewContent(
            DebugCenterOverviewLayoutResult layout,
            DebugCenterPageContext context,
            Pawn? selectedPawn,
            DebugCenterOverviewModel model)
        {
            ISettingsProvider? settings = _settings ?? RuntimeServiceHub.Shared.Capture().GetOptional<ISettingsProvider>();
            IRequestQueue? queue = _requestQueue ?? RuntimeServiceHub.Shared.Capture().GetOptional<IRequestQueue>();
            IClientManager? clientManager = _clientManager ?? RuntimeServiceHub.Shared.Capture().GetOptional<IClientManager>();

            Rect canvas = new Rect(0f, 0f, layout.ViewRect.width, layout.ViewRect.height);
            float y = 4f;

            y = DrawLiveAiSection(canvas, y, settings, clientManager, model);
            y = DrawQueueConsoleSection(canvas, y, queue, settings);
            y = DrawSettingsTunerSection(canvas, y, settings);
            y = DrawColonistInspectorSection(canvas, y, context, selectedPawn);
            DrawQuickNavigationSection(canvas, y, context);
        }

        private static float DrawLiveAiSection(
            Rect canvas,
            float y,
            ISettingsProvider? settings,
            IClientManager? clientManager,
            DebugCenterOverviewModel model)
        {
            y = SettingsUIDrawer.DrawSectionHeader(
                canvas,
                y,
                "RimMind.UI.Hub.LiveAIConnectivity".Translate(),
                "RimMind.UI.Hub.LiveAIConnectivityTip".Translate());

            float cardW = canvas.width - 8f;
            Rect card = new Rect(4f, y, cardW, 94f);
            Widgets.DrawBoxSolid(card, RimMindUI.ColorCardBg);

            float col1W = cardW * 0.46f;
            float col2W = cardW - col1W - 20f;
            float leftX = card.x + 8f;
            float rightX = leftX + col1W + 12f;

            // Left column: Provider, Model, Endpoint
            string providerStr = settings?.Provider ?? "Unknown";
            string modelStr = settings?.ModelName ?? "-";
            string endpointStr = settings?.ApiEndpoint ?? "-";

            string lifecycleTip = $"{"RimMind.UI.Hub.Lifecycle.Title".Translate()}:\n" +
                $"{"RimMind.UI.Hub.Lifecycle.RuntimeState".Translate()}: {LocalizeLifecycleState(model.RuntimeDiagnostics?.State)} " +
                $"({"RimMind.UI.Hub.Lifecycle.Generation".Translate()}: {model.RuntimeDiagnostics?.Generation ?? 0}, {"RimMind.UI.Hub.Lifecycle.ServiceCount".Translate()}: {model.RuntimeDiagnostics?.ServiceCount ?? 0})\n" +
                $"{"RimMind.UI.Hub.Lifecycle.GameState".Translate()}: {LocalizeLifecycleState(model.GameDiagnostics?.State)} " +
                $"({"RimMind.UI.Hub.Lifecycle.Generation".Translate()}: {model.GameDiagnostics?.Generation ?? 0})\n" +
                $"{"RimMind.UI.Hub.Lifecycle.PublishedAt".Translate()}: {(model.RuntimeDiagnostics?.PublishedAtUtc?.ToString("HH:mm:ss") ?? "RimMind.UI.Hub.Lifecycle.Never".Translate().RawText)}\n" +
                $"{"RimMind.UI.Hub.Lifecycle.RuntimeId".Translate()}: {(model.RuntimeDiagnostics != null ? model.RuntimeDiagnostics.RuntimeId.ToString() : "RimMind.UI.Hub.Lifecycle.None".Translate().RawText)}\n" +
                $"{"RimMind.UI.Hub.Lifecycle.StaleDiscards".Translate()}: {model.RuntimeDiagnostics?.StaleCompletionDiscardCount ?? 0}\n" +
                $"{"RimMind.UI.Hub.Lifecycle.LastFailure".Translate()}: {(model.RuntimeDiagnostics?.LastBuildFailureSummary ?? "RimMind.UI.Hub.Lifecycle.None".Translate().RawText)}";

            Rect provRect = new Rect(leftX, card.y + 6f, col1W, 20f);
            Text.Font = GameFont.Small;
            Widgets.Label(provRect, $"{"RimMind.Settings.Provider".Translate()}: {providerStr} ({modelStr})");
            TooltipHandler.TipRegion(provRect, $"Endpoint: {endpointStr}\n\n{lifecycleTip}");

            Rect endpRect = new Rect(leftX, card.y + 26f, col1W, 18f);
            Text.Font = GameFont.Tiny;
            GUI.color = RimMindUI.ColorMuted;
            Widgets.Label(endpRect, endpointStr.Truncate(col1W));
            TooltipHandler.TipRegion(endpRect, endpointStr);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Test Button (height 28f)
            Rect testBtnRect = new Rect(leftX, card.y + 54f, 210f, 28f);
            bool isTesting = LiveAiProbeState.IsTesting;
            string testBtnText = isTesting
                ? "⏳ " + "RimMind.Settings.Status.Testing".Translate()
                : "RimMind.UI.Hub.TestAIRequest".Translate();

            if (Widgets.ButtonText(testBtnRect, testBtnText) && !isTesting)
            {
                if (settings != null)
                {
                    LiveAiProbeState.TriggerProbe(settings, clientManager);
                }
            }
            TooltipHandler.TipRegion(testBtnRect, "RimMind.UI.Hub.TestAIRequestTip".Translate());

            // Right column: Ping Status, Latency & Tokens, Snippet / Error
            float rightY = card.y + 6f;
            Rect statusRow = new Rect(rightX, rightY, col2W, 20f);
            GUI.color = LiveAiProbeState.StatusColor;
            Widgets.Label(statusRow, $"{"RimMind.UI.Hub.PingStatus".Translate()}: {LiveAiProbeState.LastStatus}");
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(LiveAiProbeState.LastError))
            {
                TooltipHandler.TipRegion(statusRow, LiveAiProbeState.LastError);
            }

            rightY += 22f;
            Rect statsRow = new Rect(rightX, rightY, col2W, 18f);
            if (LiveAiProbeState.LastLatencyMs > 0)
            {
                Widgets.Label(statsRow, $"{"RimMind.UI.Hub.Latency".Translate()}: {LiveAiProbeState.LastLatencyMs}ms | {"RimMind.UI.Hub.Tokens".Translate()}: {LiveAiProbeState.LastTokensUsed}");
                TooltipHandler.TipRegion(statsRow, $"RTT: {LiveAiProbeState.LastLatencyMs}ms, Tokens: {LiveAiProbeState.LastTokensUsed}");
            }
            else
            {
                GUI.color = RimMindUI.ColorMuted;
                Widgets.Label(statsRow, "RimMind.UI.Hub.PingNotRun".Translate());
                GUI.color = Color.white;
                TooltipHandler.TipRegion(statsRow, "RimMind.UI.Hub.PingNotRun".Translate());
            }

            rightY += 20f;
            Rect snipRow = new Rect(rightX, rightY, col2W, 36f);
            Text.Font = GameFont.Tiny;
            if (!string.IsNullOrEmpty(LiveAiProbeState.LastSnippet))
            {
                GUI.color = RimMindUI.ColorValue;
                Widgets.Label(snipRow, $"{"RimMind.UI.Hub.ResponseSnippet".Translate()}: \"{LiveAiProbeState.LastSnippet.Truncate(col2W * 1.6f)}\"");
                TooltipHandler.TipRegion(snipRow, LiveAiProbeState.LastSnippet);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(LiveAiProbeState.LastError))
            {
                GUI.color = RimMindUI.ColorError;
                Widgets.Label(snipRow, LiveAiProbeState.LastError.Truncate(col2W * 1.6f));
                TooltipHandler.TipRegion(snipRow, LiveAiProbeState.LastError);
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;

            return card.yMax + 12f;
        }

        private static float DrawQueueConsoleSection(
            Rect canvas,
            float y,
            IRequestQueue? queue,
            ISettingsProvider? settings)
        {
            y = SettingsUIDrawer.DrawSectionHeader(
                canvas,
                y,
                "RimMind.UI.Hub.QueueAndCooldownTitle".Translate(),
                "RimMind.UI.Hub.QueueAndCooldownTip".Translate());

            float cardW = canvas.width - 8f;
            Rect card = new Rect(4f, y, cardW, 82f);
            Widgets.DrawBoxSolid(card, RimMindUI.ColorCardBg);

            int queued = queue?.TotalQueuedCount ?? 0;
            int active = queue?.ActiveRequestCount ?? 0;
            int maxConc = settings?.MaxConcurrentRequests ?? 1;
            bool isPaused = queue?.IsPaused == true;
            bool localBusy = queue?.IsLocalModelBusy == true;

            float colW = (cardW - 24f) / 3f;

            // Row 1: Active Concurrency, Queued Count, Dispatch State
            Rect col1 = new Rect(card.x + 8f, card.y + 6f, colW, 20f);
            Widgets.Label(col1, $"{"RimMind.UI.Hub.ActiveRequests".Translate()}: {active} / {maxConc}");
            TooltipHandler.TipRegion(col1, "RimMind.UI.Hub.MaxConcurrentTip".Translate());

            Rect col2 = new Rect(col1.xMax + 8f, card.y + 6f, colW, 20f);
            Widgets.Label(col2, $"{"RimMind.Settings.Queue.Queued".Translate()}: {queued}");
            TooltipHandler.TipRegion(col2, "RimMind.UI.Hub.ClearQueuesTip".Translate());

            Rect col3 = new Rect(col2.xMax + 8f, card.y + 6f, colW, 20f);
            string localModelText = localBusy ? "RimMind.Settings.Queue.Busy".Translate() : "RimMind.Settings.Queue.Idle".Translate();
            GUI.color = isPaused ? RimMindUI.ColorPaused : RimMindUI.ColorActive;
            Widgets.Label(col3, $"{(isPaused ? "RimMind.Settings.QueuePaused".Translate() : "RimMind.Settings.QueueRunning".Translate())} | {localModelText}");
            GUI.color = Color.white;
            TooltipHandler.TipRegion(col3, "RimMind.UI.Hub.TogglePauseTip".Translate());

            // Row 2: Action Buttons (height 28f)
            float btnY = card.y + 42f;
            float btnW = (cardW - 32f) / 3f;

            Rect btnPause = new Rect(card.x + 8f, btnY, btnW, 28f);
            string pauseLabel = isPaused
                ? "▶️ " + "RimMind.Settings.Queue.Resume".Translate()
                : "⏸️ " + "RimMind.Settings.Queue.Pause".Translate();
            if (Widgets.ButtonText(btnPause, pauseLabel))
            {
                if (queue != null)
                {
                    if (queue.IsPaused) queue.ResumeQueue();
                    else queue.PauseQueue();
                }
            }
            TooltipHandler.TipRegion(btnPause, "RimMind.UI.Hub.TogglePauseTip".Translate());

            Rect btnClearQ = new Rect(btnPause.xMax + 8f, btnY, btnW, 28f);
            if (Widgets.ButtonText(btnClearQ, "🗑️ " + "RimMind.Settings.Queue.ClearQueues".Translate()))
            {
                queue?.ClearAllQueues();
                Messages.Message("RimMind.UI.Hub.QueueFlushed".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            TooltipHandler.TipRegion(btnClearQ, "RimMind.UI.Hub.ClearQueuesTip".Translate());

            Rect btnClearCd = new Rect(btnClearQ.xMax + 8f, btnY, btnW, 28f);
            if (Widgets.ButtonText(btnClearCd, "❄️ " + "RimMind.Settings.Queue.ClearCooldowns".Translate()))
            {
                queue?.ClearAllCooldowns();
                Messages.Message("RimMind.UI.Hub.CooldownsCleared".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            TooltipHandler.TipRegion(btnClearCd, "RimMind.UI.Hub.ClearCooldownsTip".Translate());

            return card.yMax + 12f;
        }

        private static float DrawSettingsTunerSection(
            Rect canvas,
            float y,
            ISettingsProvider? settings)
        {
            y = SettingsUIDrawer.DrawSectionHeader(
                canvas,
                y,
                "RimMind.UI.Hub.RuntimeSettingsTuner".Translate(),
                "RimMind.UI.Hub.RuntimeSettingsTunerTip".Translate());

            float cardW = canvas.width - 8f;
            Rect card = new Rect(4f, y, cardW, 90f);
            Widgets.DrawBoxSolid(card, RimMindUI.ColorCardBg);

            if (settings != null)
            {
                float halfW = (cardW - 24f) / 2f;
                float rY = card.y + 8f;

                // Row 1 Left: Max Concurrent Slider (1 ~ 5)
                Rect concLabelRect = new Rect(card.x + 8f, rY, halfW * 0.5f, 22f);
                int curConc = settings.MaxConcurrentRequests;
                Widgets.Label(concLabelRect, $"{"RimMind.Settings.MaxConcurrentRequests".Translate()}: {curConc}");
                Widgets.Label(concLabelRect, $"{"RimMind.Settings.MaxConcurrent".Translate()}: {curConc}");
                TooltipHandler.TipRegion(concLabelRect, "RimMind.UI.Hub.MaxConcurrentTip".Translate());

                Rect concSliderRect = new Rect(concLabelRect.xMax + 4f, rY, halfW * 0.5f - 4f, 22f);
                float newConc = Widgets.HorizontalSlider(concSliderRect, curConc, 1f, 5f, roundTo: 1f);
                if ((int)newConc != curConc)
                {
                    settings.MaxConcurrentRequests = (int)newConc;
                    settings.Persist();
                }
                TooltipHandler.TipRegion(concSliderRect, "RimMind.UI.Hub.MaxConcurrentTip".Translate());

                // Row 1 Right: Request Timeout Slider (15s ~ 120s)
                float rightColX = card.x + halfW + 16f;
                Rect timeLabelRect = new Rect(rightColX, rY, halfW * 0.5f, 22f);
                int curTimeoutSec = Mathf.Clamp(settings.RequestTimeoutMs / 1000, 15, 120);
                Widgets.Label(timeLabelRect, $"{"RimMind.Settings.RequestTimeout".Translate()}: {curTimeoutSec}s");
                TooltipHandler.TipRegion(timeLabelRect, "RimMind.UI.Hub.RequestTimeoutTip".Translate());

                Rect timeSliderRect = new Rect(timeLabelRect.xMax + 4f, rY, halfW * 0.5f - 4f, 22f);
                float newTimeout = Widgets.HorizontalSlider(timeSliderRect, curTimeoutSec, 15f, 120f, roundTo: 5f);
                if ((int)newTimeout != curTimeoutSec)
                {
                    settings.RequestTimeoutMs = (int)newTimeout * 1000;
                    settings.Persist();
                }
                TooltipHandler.TipRegion(timeSliderRect, "RimMind.UI.Hub.RequestTimeoutTip".Translate());

                rY += 34f;

                // Row 2 Left: Verbose Dev Logging Checkbox
                Rect logRect = new Rect(card.x + 8f, rY, halfW, 24f);
                bool debugLog = settings.DebugLogging;
                Widgets.CheckboxLabeled(logRect, "RimMind.Settings.DebugLogging".Translate(), ref debugLog);
                if (debugLog != settings.DebugLogging)
                {
                    settings.DebugLogging = debugLog;
                    settings.Persist();
                }
                TooltipHandler.TipRegion(logRect, "RimMind.UI.Hub.DebugLoggingTip".Translate());

                // Row 2 Right: Mock / Offline Mode Checkbox
                Rect mockRect = new Rect(rightColX, rY, halfW, 24f);
                bool mockMode = LiveAiProbeState.OfflineSimulationMode;
                Widgets.CheckboxLabeled(mockRect, "RimMind.UI.Hub.MockMode".Translate(), ref mockMode);
                if (mockMode != LiveAiProbeState.OfflineSimulationMode)
                {
                    LiveAiProbeState.OfflineSimulationMode = mockMode;
                }
                TooltipHandler.TipRegion(mockRect, "RimMind.UI.Hub.MockModeTip".Translate());
            }

            return card.yMax + 12f;
        }

        private static float DrawColonistInspectorSection(
            Rect canvas,
            float y,
            DebugCenterPageContext context,
            Pawn? selectedPawn)
        {
            y = SettingsUIDrawer.DrawSectionHeader(
                canvas,
                y,
                "RimMind.UI.Hub.ColonistAgentInspector".Translate(),
                "RimMind.UI.Hub.ColonistAgentInspectorTip".Translate());

            float cardW = canvas.width - 8f;
            Rect card = new Rect(4f, y, cardW, 108f);
            Widgets.DrawBoxSolid(card, RimMindUI.ColorCardBg);

            float pY = card.y + 8f;
            string pawnName = selectedPawn != null ? selectedPawn.LabelShortCap : "RimMind.UI.Hub.NoPawn".Translate();

            // Row 1: Selected Pawn label + Selector Button
            Rect pLabelRect = new Rect(card.x + 8f, pY, 200f, 26f);
            GUI.color = selectedPawn != null ? RimMindUI.ColorValue : RimMindUI.ColorMuted;
            Widgets.Label(pLabelRect, $"{"RimMind.UI.Hub.SelectedPawn".Translate()}: {pawnName}");
            GUI.color = Color.white;
            TooltipHandler.TipRegion(pLabelRect, "RimMind.UI.Hub.SelectColonistTip".Translate());

            Rect pSelectBtn = new Rect(card.x + 214f, pY, 130f, 26f);
            if (Widgets.ButtonText(pSelectBtn, "RimMind.UI.Hub.SelectColonist".Translate()))
            {
                var colonists = Find.CurrentMap?.mapPawns?.FreeColonists?.Where(p => !p.Dead).ToList();
                if (colonists != null && colonists.Count > 0)
                {
                    var options = colonists.Select(p => new FloatMenuOption(p.LabelShortCap, () =>
                    {
                        context.SelectedPawn = p;
                    })).ToList();
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            TooltipHandler.TipRegion(pSelectBtn, "RimMind.UI.Hub.SelectColonistTip".Translate());

            if (selectedPawn != null)
            {
                var comp = CompPawnAgent.GetComp(selectedPawn);
                var agent = comp?.Agent;

                string stateStr = agent?.State.ToString() ?? "Not Initialized";
                Color stateCol = agent?.State == AgentState.Active ? RimMindUI.ColorActive :
                                 agent?.State == AgentState.Paused ? RimMindUI.ColorPaused : RimMindUI.ColorMuted;

                Rect stateRect = new Rect(card.x + 356f, pY, cardW - 364f, 26f);
                GUI.color = stateCol;
                Widgets.Label(stateRect, $"{"RimMind.UI.DebugTable.Header.Status".Translate()}: {stateStr}");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(stateRect, $"Agent State: {stateStr}");

                pY += 30f;

                // Row 2: Workflow & Last Think Tick
                string workflowStr = agent?.WorkflowPhase.ToString() ?? "-";
                string lastThinkStr = agent?.LastThinkTick > 0
                    ? $"{Find.TickManager.TicksGame - agent.LastThinkTick.Value} ticks ago"
                    : "Never";

                Rect wfRect = new Rect(card.x + 8f, pY, cardW * 0.5f - 12f, 22f);
                Widgets.Label(wfRect, $"{"RimMind.UI.Hub.AgentWorkflow".Translate()}: {workflowStr} ({"RimMind.UI.Hub.AgentAutonomy".Translate()}: {agent?.AutonomyLevel})");
                TooltipHandler.TipRegion(wfRect, $"Phase: {workflowStr}, Autonomy: {agent?.AutonomyLevel}");

                Rect tickRect = new Rect(card.x + cardW * 0.5f + 4f, pY, cardW * 0.5f - 12f, 22f);
                Widgets.Label(tickRect, $"{"RimMind.UI.Hub.AgentLoopLastTick".Translate()}: {lastThinkStr}");
                TooltipHandler.TipRegion(tickRect, $"Last think: {lastThinkStr}");

                pY += 28f;

                // Row 3: Inspect Payload & Trigger Agent Tick
                float btnW = (cardW - 24f) / 2f;
                Rect btnInspect = new Rect(card.x + 8f, pY, btnW, 28f);
                if (Widgets.ButtonText(btnInspect, "RimMind.UI.Hub.InspectPayload".Translate()))
                {
                    Find.WindowStack.Add(new Window_ContextPayloadInspector(selectedPawn));
                }
                TooltipHandler.TipRegion(btnInspect, "RimMind.UI.Hub.InspectPayloadTip".Translate());

                Rect btnTick = new Rect(btnInspect.xMax + 8f, pY, btnW, 28f);
                if (Widgets.ButtonText(btnTick, "RimMind.UI.Hub.TriggerAgentTick".Translate()))
                {
                    comp?.EnsureAgentCreated();
                    if (comp?.Agent != null)
                    {
                        comp.Agent.ForceThink();
                        comp.Agent.Tick();
                        Messages.Message(string.Format("RimMind.UI.Hub.AgentTickTriggered".Translate(), selectedPawn.LabelShortCap), MessageTypeDefOf.TaskCompletion, false);
                    }
                    else
                    {
                        Messages.Message("RimMind.UI.Hub.AgentNotFound".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                }
                TooltipHandler.TipRegion(btnTick, "RimMind.UI.Hub.TriggerAgentTickTip".Translate());
            }
            else
            {
                pY += 34f;
                Rect hintRect = new Rect(card.x + 8f, pY, cardW - 16f, 24f);
                GUI.color = RimMindUI.ColorMuted;
                Widgets.Label(hintRect, "RimMind.UI.Hub.NoPawn".Translate() + " - " + "RimMind.UI.Hub.SelectColonistTip".Translate());
                GUI.color = Color.white;
                TooltipHandler.TipRegion(hintRect, "RimMind.UI.Hub.SelectColonistTip".Translate());
            }

            return card.yMax + 12f;
        }

        private static void DrawQuickNavigationSection(
            Rect canvas,
            float y,
            DebugCenterPageContext context)
        {
            SettingsUIDrawer.DrawSectionHeader(
                canvas,
                y,
                "RimMind.UI.Hub.QuickActions".Translate(),
                null);

            float cardW = canvas.width - 8f;
            float btnW = (cardW - 16f) / 3f;
            float btnH = 28f;
            float row1Y = y + 36f;

            Rect nav1 = new Rect(4f, row1Y, btnW, btnH);
            if (Widgets.ButtonText(nav1, "RimMind.UI.Hub.Tab.Agents".Translate()))
                context.Navigation.GoTo("agents");

            Rect nav2 = new Rect(nav1.xMax + 8f, row1Y, btnW, btnH);
            if (Widgets.ButtonText(nav2, "RimMind.UI.Hub.Tab.AIRequests".Translate()))
                context.Navigation.GoTo("ai_requests");

            Rect nav3 = new Rect(nav2.xMax + 8f, row1Y, btnW, btnH);
            if (Widgets.ButtonText(nav3, "RimMind.UI.Hub.Tab.ToolCalls".Translate()))
                context.Navigation.GoTo("tool_calls");

            float row2Y = row1Y + btnH + 6f;

            Rect nav4 = new Rect(4f, row2Y, btnW, btnH);
            if (Widgets.ButtonText(nav4, "RimMind.UI.Hub.Tab.Mechanisms".Translate()))
                context.Navigation.GoTo("mechanisms");

            Rect nav5 = new Rect(nav4.xMax + 8f, row2Y, btnW, btnH);
            if (Widgets.ButtonText(nav5, "RimMind.UI.Hub.Tab.ContextKeys".Translate()))
                context.Navigation.GoTo("context_keys");

            Rect nav6 = new Rect(nav5.xMax + 8f, row2Y, btnW, btnH);
            if (Widgets.ButtonText(nav6, "RimMind.UI.Hub.OpenSettings".Translate()))
                Find.WindowStack.Add(new Window_RimMindSettings());
        }

        private DebugCenterOverviewModel BuildModel(Pawn? selectedPawn)
        {
            AgentLoopSnapshot loop = _agentLoopScheduler?.GetSnapshot() ?? AgentLoopSnapshot.Empty;

            string queueText = _requestQueue == null
                ? "RimMind.UI.Hub.QueueMissing".Translate()
                : _requestQueue.IsPaused
                    ? "RimMind.Settings.QueuePaused".Translate()
                    : "RimMind.Settings.QueueRunning".Translate();

            var model = new DebugCenterOverviewModel(
                loop.ActiveAgents,
                loop.PausedAgents,
                loop.DormantAgents,
                loop.TerminatedAgents,
                RequestOverlay.Pending.Count,
                queueText,
                selectedPawn?.LabelShortCap ?? "RimMind.UI.Hub.NoPawn".Translate(),
                loop.RegisteredPawnAgents,
                loop.RegisteredScopedAgents,
                loop.LastTick,
                loop.FaultedAgents);
            model.AttachLifecycleDiagnostics(
                RuntimeServiceHub.Shared.GetDiagnostics(),
                GameServiceHub.Shared.GetDiagnostics());
            return model;
        }

        private static string LocalizeLifecycleState(RuntimeLifecycleState? state)
            => state switch
            {
                RuntimeLifecycleState.NeverPublished => "RimMind.UI.Lifecycle.NeverPublished".Translate(),
                RuntimeLifecycleState.Building => "RimMind.UI.Lifecycle.Building".Translate(),
                RuntimeLifecycleState.Running => "RimMind.UI.Lifecycle.Running".Translate(),
                RuntimeLifecycleState.Stopped => "RimMind.UI.Lifecycle.Stopped".Translate(),
                RuntimeLifecycleState.Failed => "RimMind.UI.Lifecycle.Failed".Translate(),
                _ => string.Empty
            };

        private static string LocalizeLifecycleState(GameLifecycleState? state)
            => state switch
            {
                GameLifecycleState.NeverPublished => "RimMind.UI.Lifecycle.NeverPublished".Translate(),
                GameLifecycleState.Running => "RimMind.UI.Lifecycle.Running".Translate(),
                GameLifecycleState.Stopped => "RimMind.UI.Lifecycle.Stopped".Translate(),
                GameLifecycleState.Failed => "RimMind.UI.Lifecycle.Failed".Translate(),
                _ => string.Empty
            };
    }
}
