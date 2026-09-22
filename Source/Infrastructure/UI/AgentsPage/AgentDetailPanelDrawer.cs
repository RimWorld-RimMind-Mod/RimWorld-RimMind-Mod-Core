using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentDetailPanelDrawer
    {
        private readonly RuntimeServiceRef<IAIRequestTraceLog> _traceLog =
            RuntimeServiceRef<IAIRequestTraceLog>.Optional();

        public AgentPageViewModel? BuildViewModel(Pawn? selectedPawn)
        {
            if (selectedPawn == null)
                return null;

            var comp = CompPawnAgent.GetComp(selectedPawn);
            var traceRows = BuildTraceRows();
            if (comp?.Agent == null)
            {
                return AgentPageViewModel.PendingCreation(
                    selectedPawn.LabelShortCap,
                    RequestOverlay.Pending.Count,
                    traceRows);
            }

            return AgentPageViewModel.FromState(
                selectedPawn.LabelShortCap,
                comp.Agent.State,
                RequestOverlay.Pending.Count,
                requestRows: 0,
                traceRows);
        }

        public void Draw(AgentPageRects layout, Pawn? selectedPawn, RimMindLayoutScope scope)
        {
            scope.Record(layout.Detail, "Agents:DetailPanel");
            Widgets.DrawBoxSolid(layout.Detail, RimMindUI.ColorCardBg);

            if (selectedPawn == null)
            {
                RimMindUI.DrawEmptyState(layout.Detail, "RimMind.UI.AgentStateDebug.NoPawn".Translate());
                return;
            }

            var comp = CompPawnAgent.GetComp(selectedPawn);
            DrawDetailHeader(layout.Status, selectedPawn, comp?.Agent);

            if (comp?.Agent == null)
            {
                Rect createRect = layout.ActionBar.Buttons.Count > 0
                    ? layout.ActionBar.Buttons[0].Rect
                    : new Rect(layout.Actions.x, layout.Actions.y, 160f, RimMindUI.BtnHeight);
                if (Widgets.ButtonText(createRect, "RimMind.UI.AgentsPage.CreateStart".Translate()))
                {
                    if (comp != null && comp.EnsureAgentCreated())
                        SafeTransitionTo(comp.Agent, AgentState.Active);
                    else
                        Messages.Message("RimMind.UI.AgentsPage.CreateFailed".Translate(),
                            MessageTypeDefOf.RejectInput, false);
                }
                return;
            }

            var agent = comp.Agent;
            DrawActions(layout.ActionBar, agent);
        }

        private static void DrawDetailHeader(Rect rect, Pawn pawn, IAgentControl? agent)
        {
            Rect inner = rect.ContractedBy(RimMindUI.Padding);

            Text.Font = GameFont.Medium;
            GUI.color = RimMindUI.ColorHeader;
            const float nameHeight = 32f;
            Rect nameRect = new Rect(inner.x, inner.y, inner.width, nameHeight);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(nameRect, pawn.Name != null ? pawn.Name.ToStringFull : pawn.LabelCap);

            string stateLabel = "RimMind.UI.AgentsPage.Pending".Translate();
            AgentState? state = null;
            if (agent != null)
            {
                state = agent.State;
                stateLabel = AgentActivityStreamDrawer.StateLabel(agent.State);
            }

            var (textColor, bgColor) = state.HasValue
                ? RimMindUI.GetStateBadgeColors(state.Value)
                : RimMindUI.GetStateBadgeColors(AgentState.Dormant, isPendingCreation: true);

            // Pass rect (whose padding gives inner.x) so the badge aligns with inner.x
            RimMindUI.DrawStatusBadge(rect, inner.y + nameHeight + 6f,
                "RimMind.UI.AgentsPage.State".Translate() + ": " + stateLabel, textColor, bgColor);
        }

        private static void DrawActions(ActionBarLayoutResult actionBar, IAgentControl agent)
        {
            foreach (var button in actionBar.Buttons)
            {
                switch (button.Id)
                {
                    case "primary":
                        DrawPrimaryStateButton(button.Rect, agent);
                        bool isSingle = agent.State == AgentState.Dormant || agent.State == AgentState.Terminated;
                        Rect btnRect = isSingle
                            ? new Rect(button.Rect.x, button.Rect.y, Mathf.Min(button.Rect.width * 2f + RimMindUiMetrics.ButtonGap, 200f), button.Rect.height)
                            : button.Rect;
                        DrawPrimaryStateButton(btnRect, agent);
                        break;
                    case "force_think":
                        if ((agent.State == AgentState.Active || agent.State == AgentState.Paused)
                            && Widgets.ButtonText(button.Rect, "RimMind.UI.AgentsPage.ForceThink".Translate()))
                            agent.ForceThink();
                        break;
                    case "open_requests":
                        if ((agent.State == AgentState.Active || agent.State == AgentState.Paused)
                            && Widgets.ButtonText(button.Rect, "RimMind.UI.AgentsPage.OpenRequests".Translate()))
                            Find.WindowStack.Add(Window_RimMindHub.OpenAIRequests());
                        break;
                }
            }
        }

        private static void DrawPrimaryStateButton(Rect rect, IAgentControl agent)
        {
            switch (agent.State)
            {
                case AgentState.Active:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Pause".Translate()))
                        SafeTransitionTo(agent, AgentState.Paused);
                    break;
                case AgentState.Paused:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Resume".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
                case AgentState.Dormant:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Activate".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
                case AgentState.Terminated:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Restart".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
            }
        }

        private static void SafeTransitionTo(IAgentControl? agent, AgentState target)
        {
            if (agent == null) return;
            agent.TransitionTo(target);
        }

        private System.Collections.Generic.IReadOnlyList<AgentRequestTraceRow> BuildTraceRows()
        {
            var log = _traceLog.ValueOrDefault;
            return AgentRequestTraceRowBuilder.BuildRecent(log?.Entries);
        }
    }
}
