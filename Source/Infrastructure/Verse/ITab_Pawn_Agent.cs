using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Agent;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class ITab_Pawn_Agent : RimMindITabBase
    {
        private static readonly Vector2 WinSize = new Vector2(440f, 520f);

        private Vector2 _scrollPosition = Vector2.zero;

        public ITab_Pawn_Agent()
        {
            size = WinSize;
            labelKey = "RimMind.Agent.ITab.Label";
        }

        private Pawn SelectedPawn => SelThing as Pawn;

        protected override void FillTabContents(Rect inRect, RimMindLayoutScope scope)
        {
            var pawn = SelectedPawn;
            if (pawn == null) return;

            var comp = CompPawnAgent.GetComp(pawn);
            var agent = comp?.Agent;

            var rect = new Rect(RimMindUI.Padding, RimMindUI.Padding, WinSize.x - RimMindUI.Padding * 2, WinSize.y - RimMindUI.Padding * 2);
            scope.Record(rect, "Body");

            if (agent == null)
            {
                DrawNoAgentState(rect, pawn, comp, scope);
                return;
            }

            float contentH = CalculateContentHeight(agent, rect.width);
            var contentRect = new Rect(0f, 0f, rect.width - 16f, contentH);

            Widgets.BeginScrollView(rect, ref _scrollPosition, contentRect);
            scope.Record(rect, "ScrollView:Outer");
            scope.Record(contentRect, "ScrollView:Content");

            float curY = 0f;

            // ── Section: Status ──
            curY = RimMindUI.DrawSectionHeader(contentRect, curY, "RimMind.Agent.ITab.Status".Translate());

            var (stateTextColor, stateBgColor) = RimMindUI.GetStateBadgeColors(
                agent.State == AgentState.Active,
                agent.State == AgentState.Paused);
            string stateKey = $"RimMind.Agent.State.{agent.State}";
            string stateLabel = stateKey.Translate();
            curY = RimMindUI.DrawStatusBadge(contentRect, curY, stateLabel, stateTextColor, stateBgColor);

            curY = RimMindUI.DrawKeyValueRow(contentRect, curY, "Mode", agent.CurrentModeId.Value);

            curY = RimMindUI.DrawKeyValueRow(contentRect, curY, "WorkflowPhase", agent.WorkflowPhase.ToString());
            curY = RimMindUI.DrawKeyValueRow(contentRect, curY, "Autonomy", agent.AutonomyLevel.ToString());

            // ── Section: Goals ──
            curY = RimMindUI.DrawDivider(contentRect, curY);
            curY = RimMindUI.DrawSectionHeader(contentRect, curY, "RimMind.Agent.ITab.Goals".Translate());

            var goals = agent.GoalStack.Goals;
            if (goals.Count == 0)
            {
                curY = RimMindUI.DrawWrappedLabel(contentRect, curY, "  (none)", RimMindUI.ColorMuted);
            }
            else
            {
                foreach (var goal in goals)
                {
                    string statusMarker = goal.Status.ToString();
                    Color markerColor = goal.Status == GoalStatus.Achieved
                        ? RimMindUI.ColorActive
                        : goal.Status == GoalStatus.Abandoned
                            ? RimMindUI.ColorError
                            : RimMindUI.ColorValue;
                    string goalText = $"[{statusMarker}] {goal.Description} (P:{goal.Priority:F1})";
                    curY = RimMindUI.DrawWrappedLabel(contentRect, curY, goalText, markerColor);
                }
            }

            // ── Section: Strategy ──
            curY = RimMindUI.DrawDivider(contentRect, curY);
            curY = RimMindUI.DrawSectionHeader(contentRect, curY, "RimMind.Agent.ITab.Strategy".Translate());

            var topW = agent.StrategyOptimizer.GetTopN(5);
            if (topW.Count == 0)
            {
                curY = RimMindUI.DrawWrappedLabel(contentRect, curY, "  (no data)", RimMindUI.ColorMuted);
            }
            else
            {
                foreach (var kv in topW)
                {
                    curY = RimMindUI.DrawKeyValueRow(contentRect, curY, kv.Key, kv.Value.ToString("F2"));
                }
            }

            // ── Section: History ──
            curY = RimMindUI.DrawDivider(contentRect, curY);
            curY = RimMindUI.DrawSectionHeader(contentRect, curY, "RimMind.Agent.ITab.History".Translate());

            var recent = agent.GetRecentHistory(5);
            if (recent.Count == 0)
            {
                curY = RimMindUI.DrawWrappedLabel(contentRect, curY, "  (no history)", RimMindUI.ColorMuted);
            }
            else
            {
                Text.Font = GameFont.Tiny;
                foreach (var record in recent)
                {
                    var marker = record.Success ? "OK" : "FAIL";
                    Color markerColor = record.Success ? RimMindUI.ColorActive : RimMindUI.ColorError;
                    string recordText = $"[{marker}] {record.Action} - {record.Reason}";
                    curY = RimMindUI.DrawWrappedLabel(contentRect, curY, recordText, markerColor);
                }
                Text.Font = GameFont.Small;
            }

            if (Prefs.DevMode)
            {
                curY = RimMindUI.DrawDivider(contentRect, curY);
                Rect inspectBtn = new Rect(contentRect.x, curY, 180f, 26f);
                if (Widgets.ButtonText(inspectBtn, "RimMind.Inspector.OpenButton".Translate()))
                {
                    Find.WindowStack.Add(new Window_ContextPayloadInspector(agent.Pawn));
                }
                curY += 30f;
            }

            Widgets.EndScrollView();
        }

        private void DrawNoAgentState(Rect rect, Pawn pawn, CompPawnAgent? comp, RimMindLayoutScope scope)
        {
            float y = rect.y;

            scope.Record(rect, "EmptyState:NoAgent");
            RimMindUI.DrawEmptyState(rect, "RimMind.Agent.ITab.NoAgent".Translate(),
                "RimMind.Agent.ITab.NoAgentHint".Translate());

            y = rect.y + rect.height - 40f;
            Rect createBtn = new Rect(rect.x, y, 160f, 28f);
            scope.Record(createBtn, "Button:CreateAgent");
            if (Widgets.ButtonText(createBtn, "RimMind.Agent.ITab.CreateAgent".Translate()))
            {
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                var factory = runtimeScope.GetOptional<IPawnAgentFactoryVerse>();
                var agentBus = runtimeScope.GetOptional<IAgentBus>();
                if (factory != null && agentBus != null)
                {
                    var createdAgent = factory.Create(pawn, agentBus);
                    if (createdAgent != null && RuntimeServiceHub.Shared.IsCurrent(runtimeScope.Token))
                    {
                        if (comp != null && comp.Agent == null)
                            comp.Agent = createdAgent as IPawnAgentVerse;
                    }
                    else if (createdAgent != null)
                    {
                        RuntimeServiceHub.Shared.RecordStaleCompletion(LifecycleEventSources.PawnAgentTab);
                        Messages.Message("RimMind.Agent.ITab.CreateFailed".Translate(),
                            MessageTypeDefOf.RejectInput, false);
                    }
                }
                else
                {
                    Messages.Message("RimMind.Agent.ITab.CreateFailed".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private float CalculateContentHeight(IPawnAgentVerse agent, float width)
        {
            float h = 0f;

            // Status section
            h += RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // header
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // badge
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // mode
            h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 2; // workflow, autonomy

            // Goals section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // divider + header
            var goals = agent.GoalStack.Goals;
            if (goals.Count == 0)
            {
                h += RimMindUI.LineHeight;
            }
            else
            {
                Text.Font = GameFont.Small;
                foreach (var goal in goals)
                {
                    string goalText = $"[{goal.Status}] {goal.Description} (P:{goal.Priority:F1})";
                    h += Text.CalcHeight(goalText, width - RimMindUI.Padding * 4) + RimMindUI.Padding * 0.5f;
                }
            }

            // Strategy section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            var topW = agent.StrategyOptimizer.GetTopN(5);
            h += topW.Count > 0
                ? (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * topW.Count
                : RimMindUI.LineHeight;

            // History section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            var recent = agent.GetRecentHistory(5);
            if (recent.Count == 0)
            {
                h += RimMindUI.LineHeight;
            }
            else
            {
                Text.Font = GameFont.Tiny;
                foreach (var record in recent)
                {
                    string recordText = $"[{(record.Success ? "OK" : "FAIL")}] {record.Action} - {record.Reason}";
                    h += Text.CalcHeight(recordText, width - RimMindUI.Padding * 4) + RimMindUI.Padding * 0.5f;
                }
                Text.Font = GameFont.Small;
            }

            if (Prefs.DevMode)
            {
                h += 36f;
            }

            return h + RimMindUI.Padding;
        }
    }
}
