using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentActivityStreamDrawer
    {
        private const float TraceRowHeight = 24f;
        private const float StatusStripWidth = 4f;

        private static readonly IReadOnlyList<AgentRequestTraceRow> EmptyTraceRows =
            new ReadOnlyCollection<AgentRequestTraceRow>(new List<AgentRequestTraceRow>());

        private Vector2 _activityScrollPos;
        internal void ScrollToBottom() => _activityScrollPos = new Vector2(0, 100000f);

        public void Draw(Rect rect, AgentState state, int pendingRequests, RimMindLayoutScope scope)
        {
            Draw(rect, StateLabel(state), pendingRequests, EmptyTraceRows, scope);
        }

        public void Draw(
            Rect rect,
            AgentState state,
            int pendingRequests,
            IReadOnlyList<AgentRequestTraceRow> traceRows,
            RimMindLayoutScope scope)
        {
            Draw(rect, StateLabel(state), pendingRequests, traceRows, scope);
        }

        public void Draw(Rect rect, string stateLabel, int pendingRequests, RimMindLayoutScope scope)
        {
            Draw(rect, stateLabel, pendingRequests, EmptyTraceRows, scope);
        }

        public void Draw(
            Rect rect,
            string stateLabel,
            int pendingRequests,
            IReadOnlyList<AgentRequestTraceRow> traceRows,
            RimMindLayoutScope scope)
        {
            scope.Record(rect, "Agents:Activity");
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorSectionBg);
            Rect inner = rect.ContractedBy(RimMindUI.Padding);

            int rowCount = traceRows?.Count ?? 0;
            float rowsHeight = rowCount > 0
                ? rowCount * (TraceRowHeight + RimMindUI.Padding * 0.5f)
                : RimMindUI.LineHeight * 2f;
            float contentHeight = RimMindUI.LineHeight * 4f + RimMindUI.SectionGap + rowsHeight;
            var (bodyRect, _) = RimMindUI.BeginScrollView(inner, ref _activityScrollPos,
                Mathf.Max(inner.height + 1f, contentHeight));

            float y = RimMindUI.DrawSectionHeader(bodyRect, bodyRect.y,
                "RimMind.UI.AgentsPage.Activity".Translate());
            y = RimMindUI.DrawKeyValueRow(bodyRect, y,
                "RimMind.UI.AgentsPage.State".Translate(), stateLabel);
            y = RimMindUI.DrawKeyValueRow(bodyRect, y,
                "RimMind.UI.Hub.PendingRequests".Translate(), pendingRequests.ToString());

            if (rowCount == 0)
            {
                RimMindUI.DrawWrappedLabel(bodyRect, y,
                    "RimMind.UI.AgentsPage.Activity.Empty".Translate(), RimMindUI.ColorMuted);
            }
            else
            {
                for (int i = 0; i < rowCount; i++)
                {
                    y = DrawTraceRow(bodyRect, y, traceRows![i], i, scope);
                }
            }

            Widgets.EndScrollView();
        }

        public static string StateLabel(AgentState state)
        {
            return state switch
            {
                AgentState.Active => "RimMind.UI.AgentsPage.Active".Translate(),
                AgentState.Paused => "RimMind.UI.AgentsPage.Paused".Translate(),
                AgentState.Terminated => "RimMind.UI.AgentsPage.Terminated".Translate(),
                _ => "RimMind.UI.AgentsPage.Dormant".Translate()
            };
        }

        private static float DrawTraceRow(
            Rect canvas,
            float y,
            AgentRequestTraceRow row,
            int index,
            RimMindLayoutScope scope)
        {
            float x = canvas.x + RimMindUI.Padding;
            float width = canvas.width - RimMindUI.Padding * 2f;
            Rect rowRect = new Rect(x, y, width, TraceRowHeight);
            Rect stripRect = new Rect(rowRect.x, rowRect.y, StatusStripWidth, rowRect.height);
            Rect labelRect = new Rect(
                rowRect.x + StatusStripWidth + RimMindUI.Padding,
                rowRect.y,
                rowRect.width - StatusStripWidth - RimMindUI.Padding,
                rowRect.height);

            Widgets.DrawBoxSolid(rowRect, RimMindUI.ColorCardBg);
            Widgets.DrawBoxSolid(stripRect, TraceStatusColor(row.Status));

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = row.HasError ? RimMindUI.ColorError : RimMindUI.ColorValue;
            Widgets.Label(labelRect, TraceRowLabel(row));
            string labelText = TraceRowLabel(row);
            Widgets.Label(labelRect, labelText.Truncate(labelRect.width));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rowRect, labelText);

            scope.Record(rowRect, $"Agents:Activity:TraceRow:{index}");
            return y + TraceRowHeight + RimMindUI.Padding * 0.5f;
        }

        private static string TraceRowLabel(AgentRequestTraceRow row)
        {
            string summary = row.Summary;
            string label = TraceStatusLabel(row.Status);
            if (string.IsNullOrWhiteSpace(summary))
                return label + ": " + (row.ErrorMessage ?? string.Empty);

            return row.HasError && !string.IsNullOrWhiteSpace(row.ErrorMessage)
                ? label + ": " + summary + " - " + row.ErrorMessage
                : label + ": " + summary;
        }

        private static string TraceStatusLabel(AgentRequestTraceStatus status)
        {
            return status switch
            {
                AgentRequestTraceStatus.Pending => "RimMind.UI.AgentsPage.Trace.Pending".Translate(),
                AgentRequestTraceStatus.Streaming => "RimMind.UI.AgentsPage.Trace.Pending".Translate(),
                AgentRequestTraceStatus.Success => "RimMind.UI.AgentsPage.Trace.Success".Translate(),
                AgentRequestTraceStatus.Error => "RimMind.UI.AgentsPage.Trace.Error".Translate(),
                AgentRequestTraceStatus.System => "RimMind.UI.AgentsPage.Trace.Pending".Translate(),
                _ => "RimMind.UI.AgentsPage.Trace.Pending".Translate()
            };
        }

        private static Color TraceStatusColor(AgentRequestTraceStatus status)
        {
            return status switch
            {
                AgentRequestTraceStatus.Pending => new Color(0.95f, 0.58f, 0.18f, 0.9f),
                AgentRequestTraceStatus.Streaming => new Color(0.35f, 0.62f, 0.95f, 0.9f),
                AgentRequestTraceStatus.Success => new Color(0.25f, 0.78f, 0.42f, 0.9f),
                AgentRequestTraceStatus.Error => new Color(0.9f, 0.22f, 0.18f, 0.9f),
                AgentRequestTraceStatus.System => RimMindUI.ColorMuted,
                _ => RimMindUI.ColorMuted
            };
        }
    }
}
