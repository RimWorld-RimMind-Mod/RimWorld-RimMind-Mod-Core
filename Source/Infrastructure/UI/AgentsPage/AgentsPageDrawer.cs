using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentsPageDrawer
    {
        private readonly AgentListPanelDrawer _listDrawer = new();
        private readonly AgentActivityStreamDrawer _activityDrawer = new();
        private readonly AgentDetailPanelDrawer _detailDrawer = new();
        private readonly AgentChatPanelDrawer _chatDrawer = new();
        private string _chatDraft = string.Empty;
        private string? _listSelectedPawnId;

        internal void ScrollToBottom()
        {
            _listDrawer.ScrollToBottom();
            _activityDrawer.ScrollToBottom();
        }

        public void Draw(Rect rect, Pawn? hubSelectedPawn, RimMindLayoutScope scope)
        {
            AgentPageRects layout = AgentPageLayout.Calculate(rect);
            scope.Record(layout.List, "Agents:List");
            scope.Record(layout.Activity, "Agents:ActivityPane");
            scope.Record(layout.Detail, "Agents:Detail");
            scope.Record(layout.Chat, "Agents:ChatBar");

            Pawn? listPawn = _listDrawer.Draw(layout.List, hubSelectedPawn, ref _listSelectedPawnId, scope);
            Pawn? detailPawn = listPawn ?? hubSelectedPawn;
            AgentPageViewModel? model = _detailDrawer.BuildViewModel(detailPawn);
            if (model != null)
            {
                string stateLabel = model.IsPendingCreation
                    ? "RimMind.UI.AgentsPage.Pending".Translate()
                    : AgentActivityStreamDrawer.StateLabel(model.State);
                _activityDrawer.Draw(
                    layout.Activity,
                    stateLabel,
                    model.PendingRequests,
                    model.TraceRows,
                    scope);
            }
            else
            {
                RimMindUI.DrawEmptyState(layout.Activity, "RimMind.UI.AgentStateDebug.NoPawn".Translate());
            }

            _detailDrawer.Draw(layout, detailPawn, scope);
            if (detailPawn != null && model?.CanChat == true)
                _chatDrawer.Draw(layout.Chat, detailPawn, ref _chatDraft, scope);
        }
    }
}
