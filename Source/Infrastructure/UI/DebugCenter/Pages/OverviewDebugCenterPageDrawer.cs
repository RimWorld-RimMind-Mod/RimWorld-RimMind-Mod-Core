using System;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Infrastructure.UI.DebugCenter.Overview;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class OverviewDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private IAgentLoopScheduler? _agentLoopScheduler;
        private IRequestQueue? _requestQueue;
        private Vector2 _scrollPosition;
        internal void ScrollToBottom() => _scrollPosition = new Vector2(0, 100000f);

        public IDisposable? Bind(RuntimeServiceScope scope)
        {
            _agentLoopScheduler = scope.GetOptional<IAgentLoopScheduler>();
            _requestQueue = scope.GetOptional<IRequestQueue>();
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

        private static void DrawOverviewContent(
            DebugCenterOverviewLayoutResult layout,
            DebugCenterPageContext context,
            Pawn? selectedPawn,
            DebugCenterOverviewModel model)
        {
            DrawOverviewCard(layout.Cards[0], "RimMind.Context.IncludeHealth".Translate(), BuildHealthText(model), ResolveHealthColor(model));

            DrawOverviewCard(layout.Cards[1], "RimMind.UI.Hub.AgentSummary".Translate(), BuildAgentStateSummary(model), model.ActiveAgents > 0 ? RimMindUI.ColorActive : RimMindUI.ColorMuted);

            bool isQueueRunning = model.QueueState == "RimMind.Settings.QueueRunning".Translate();
            DrawOverviewCard(layout.Cards[2], "RimMind.UI.Hub.QueueState".Translate(), model.QueueState, isQueueRunning ? RimMindUI.ColorActive : RimMindUI.ColorPaused);

            DrawOverviewCard(layout.Cards[3], "RimMind.UI.Hub.SelectedPawn".Translate(), model.SelectedObject, selectedPawn == null ? RimMindUI.ColorMuted : RimMindUI.ColorValue);

            float y = layout.Summary.y;
            y = RimMindUI.DrawKeyValueRow(layout.Summary, y, "RimMind.UI.Hub.PendingRequests".Translate(), model.PendingRequests.ToString());
            y = RimMindUI.DrawKeyValueRow(layout.Summary, y, "RimMind.UI.Hub.AgentLoop".Translate(), BuildAgentLoopSummary(model));
            y = RimMindUI.DrawKeyValueRow(layout.Summary, y, "RimMind.UI.Hub.AgentLoopLastTick".Translate(), BuildLastAgentLoopTick(model));
            RimMindUI.DrawKeyValueRow(layout.Summary, y, "RimMind.UI.Hub.AgentLoopFaults".Translate(), model.AgentLoopFaults.ToString());

            RimMindUI.DrawSectionHeader(
                layout.LifecycleHeader,
                layout.LifecycleHeader.y,
                "RimMind.UI.Hub.Lifecycle.Title".Translate());
            DrawLifecycleDiagnostics(layout.LifecycleRuntime, layout.LifecycleGame, model);

            RimMindUI.DrawSectionHeader(
                layout.QuickActionsHeader,
                layout.QuickActionsHeader.y,
                "RimMind.UI.Hub.QuickActions".Translate());

            DebugCenterToolGrid.Draw(
                layout.QuickActions,
                scope: null,
                ("RimMind.UI.Hub.Tab.Agents", () => context.Navigation.GoTo("agents")),
                ("RimMind.UI.Hub.Tab.AIRequests", () => context.Navigation.GoTo("ai_requests")),
                ("RimMind.UI.Hub.Tab.ToolCalls", () => context.Navigation.GoTo("tool_calls")),
                ("RimMind.UI.Hub.Tab.Mechanisms", () => context.Navigation.GoTo("mechanisms")));
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

        private static void DrawLifecycleDiagnostics(
            Rect runtimeRect,
            Rect gameRect,
            DebugCenterOverviewModel model)
        {
            Widgets.DrawBoxSolid(runtimeRect, RimMindUI.ColorCardBg);
            Widgets.DrawBoxSolid(gameRect, RimMindUI.ColorCardBg);

            float runtimeY = RimMindUI.Padding;
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.RuntimeState", LocalizeLifecycleState(model.RuntimeDiagnostics?.State));
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.Generation", model.RuntimeGeneration.ToString());
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.ServiceCount", model.RuntimeServiceCount.ToString());
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.PublishedAt", FormatPublished(model.RuntimePublishedAtUtc));
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.RuntimeId", model.RuntimeId.ToString());
            runtimeY = DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.LastFailure", FormatFailure(model.LastBuildFailureSummary));
            DrawLifecycleRow(runtimeRect, runtimeY, "RimMind.UI.Hub.Lifecycle.StaleDiscards", model.StaleCompletionDiscardCount.ToString());

            float gameY = RimMindUI.Padding;
            gameY = DrawLifecycleRow(gameRect, gameY, "RimMind.UI.Hub.Lifecycle.GameState", LocalizeLifecycleState(model.GameDiagnostics?.State));
            gameY = DrawLifecycleRow(gameRect, gameY, "RimMind.UI.Hub.Lifecycle.Generation", model.GameGeneration.ToString());
            gameY = DrawLifecycleRow(gameRect, gameY, "RimMind.UI.Hub.Lifecycle.ServiceCount", model.GameServiceCount.ToString());
            DrawLifecycleRow(gameRect, gameY, "RimMind.UI.Hub.Lifecycle.PublishedAt", FormatPublished(model.GamePublishedAtUtc));
        }

        private static float DrawLifecycleRow(Rect column, float y, string labelKey, string value)
        {
            const float rowHeight = 16f;
            Rect row = new Rect(column.x + RimMindUI.Padding, column.y + y, column.width - RimMindUI.Padding * 2f, rowHeight);
            string text = labelKey.Translate() + ": " + value;
            Text.Font = GameFont.Tiny;
            Widgets.Label(row, text);
            TooltipHandler.TipRegion(row, text);
            Text.Font = GameFont.Small;
            return y + rowHeight;
        }

        private static string FormatPublished(DateTimeOffset? publishedAtUtc)
            => publishedAtUtc?.ToString("u") ?? "RimMind.UI.Hub.Lifecycle.Never".Translate();

        private static string FormatFailure(string? failure)
            => string.IsNullOrWhiteSpace(failure)
                ? "RimMind.UI.Hub.Lifecycle.None".Translate()
                : failure;

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

        private static string BuildHealthText(DebugCenterOverviewModel model)
        {
            if (model.AgentLoopFaults > 0)
                return $"{model.AgentLoopFaults} {"RimMind.UI.AgentsPage.Trace.Error".Translate()}";
            if (model.PausedAgents > 0)
                return $"{model.PausedAgents} {"RimMind.Agent.State.Paused".Translate()}";
            return model.ActiveAgents > 0
                ? "RimMind.Agent.State.Active".Translate()
                : "RimMind.Prompt.Health.Healthy".Translate();
        }

        private static string BuildAgentStateSummary(DebugCenterOverviewModel model)
            => $"{model.ActiveAgents} {"RimMind.Agent.State.Active".Translate()} / "
                + $"{model.PausedAgents} {"RimMind.Agent.State.Paused".Translate()} / "
                + $"{model.DormantAgents} {"RimMind.Agent.State.Dormant".Translate()} / "
                + $"{model.TerminatedAgents} {"RimMind.Agent.State.Terminated".Translate()}";

        private static string BuildAgentLoopSummary(DebugCenterOverviewModel model)
            => $"{model.RegisteredPawnAgents} {"RimMind.UI.Hub.AgentLoopPawn".Translate()} / "
                + $"{model.RegisteredScopedAgents} {"RimMind.UI.Hub.AgentLoopScoped".Translate()}";

        private static string BuildLastAgentLoopTick(DebugCenterOverviewModel model)
            => model.LastAgentLoopTick < 0
                ? "RimMind.UI.Hub.AgentLoopNeverRun".Translate()
                : model.LastAgentLoopTick.ToString();

        private static Color ResolveHealthColor(DebugCenterOverviewModel model)
        {
            if (model.AgentLoopFaults > 0)
                return RimMindUI.ColorError;
            if (model.PausedAgents > 0)
                return RimMindUI.ColorPaused;
            return model.ActiveAgents > 0 ? RimMindUI.ColorActive : RimMindUI.ColorMuted;
        }

        private static void DrawOverviewCard(Rect card, string title, string value, Color valueColor)
        {
            Widgets.DrawBoxSolid(card, RimMindUI.ColorCardBg);
            float innerY = card.y + RimMindUI.Padding;
            GUI.color = RimMindUI.ColorSectionTitle;
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(card.x + RimMindUI.Padding, innerY, card.width - RimMindUI.Padding * 2, RimMindUI.LineHeight),
                title);
            Text.Font = GameFont.Small;
            GUI.color = valueColor;
            Widgets.Label(
                new Rect(card.x + RimMindUI.Padding, innerY + RimMindUI.LineHeight, card.width - RimMindUI.Padding * 2, card.height - RimMindUI.LineHeight - RimMindUI.Padding * 2f),
                value);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
