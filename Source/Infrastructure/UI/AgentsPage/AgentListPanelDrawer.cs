using System.Collections.Generic;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentListPanelDrawer
    {
        private readonly List<AgentListItem> _agents = new();
        private readonly Dictionary<string, Pawn> _pawnById = new();
        private Vector2 _listScrollPos;
        internal void ScrollToBottom() => _listScrollPos = new Vector2(0, 100000f);

        public Pawn? Draw(Rect rect, Pawn? hubSelectedPawn, ref string? listSelectedPawnId, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Agents:ListPanel");
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorSectionBg);

            _agents.Clear();
            _pawnById.Clear();

            var map = Find.CurrentMap;
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var comp = CompPawnAgent.GetComp(pawn);
                    if (comp?.Agent == null) continue;
                    AgentState state = comp.Agent.State;
                    string id = pawn.ThingID;
                    _agents.Add(AgentListItem.ExistingPawn(id, pawn.LabelShortCap, state));
                    _pawnById[id] = pawn;
                }
            }

            string? pendingId = hubSelectedPawn != null
                ? hubSelectedPawn.ThingID : null;
            string? pendingLabel = hubSelectedPawn?.LabelShortCap;

            var groups = AgentListBuilder.Build(_agents, pendingId, pendingLabel);

            if (hubSelectedPawn != null && listSelectedPawnId == null)
                listSelectedPawnId = hubSelectedPawn.ThingID;

            Rect innerRect = rect.ContractedBy(RimMindUI.Padding);
            float contentH = Mathf.Max(innerRect.height + 1f, CalcListHeight(groups));
            var (bodyRect, _) = RimMindUI.BeginScrollView(innerRect, ref _listScrollPos, contentH);

            float y = bodyRect.y;

            float rowHeight = 24f;
            y = RimMindUI.DrawSectionHeader(bodyRect, y,
                "RimMind.UI.AgentsPage.Active".Translate() + $" ({groups.Active.Count})");
            foreach (var item in groups.Active)
                y = DrawAgentRow(new Rect(bodyRect.x, y, bodyRect.width, rowHeight), item, ref listSelectedPawnId);

            y += RimMindUI.Padding;

            y = RimMindUI.DrawSectionHeader(bodyRect, y,
                "RimMind.UI.AgentsPage.Paused".Translate() + $" ({groups.Paused.Count})");
            foreach (var item in groups.Paused)
                y = DrawAgentRow(new Rect(bodyRect.x, y, bodyRect.width, rowHeight), item, ref listSelectedPawnId);

            y += RimMindUI.Padding;

            if (groups.PendingCreation.Count > 0)
            {
                y = RimMindUI.DrawSectionHeader(bodyRect, y,
                    "RimMind.UI.AgentsPage.Pending".Translate());
                foreach (var item in groups.PendingCreation)
                    y = DrawAgentRow(new Rect(bodyRect.x, y, bodyRect.width, rowHeight), item, ref listSelectedPawnId);
            }

            if (groups.Other.Count > 0)
            {
                y += RimMindUI.Padding;
                y = RimMindUI.DrawSectionHeader(bodyRect, y,
                    "RimMind.UI.AgentsPage.Other".Translate() + $" ({groups.Other.Count})");
                foreach (var item in groups.Other)
                    y = DrawAgentRow(new Rect(bodyRect.x, y, bodyRect.width, rowHeight), item, ref listSelectedPawnId);
            }

            Widgets.EndScrollView();

            if (listSelectedPawnId != null && _pawnById.TryGetValue(listSelectedPawnId, out Pawn? listPawn))
                return listPawn;

            return null;
        }

        private float DrawAgentRow(Rect rect, AgentListItem item, ref string? listSelectedPawnId)
        {
            var (textColor, bgColor) = RimMindUI.GetStateBadgeColors(item.State, item.IsPendingCreation);

            bool isSelected = item.Id == listSelectedPawnId;

            if (isSelected)
                Widgets.DrawBoxSolid(rect, RimMindUI.ColorTabActive);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, RimMindUI.ColorTabHover);
            else
                Widgets.DrawBoxSolid(rect, bgColor);

            GUI.color = textColor;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect textRect = new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height);
            Widgets.Label(textRect, item.Label.Truncate(textRect.width));
            Text.Anchor = prevAnchor;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, item.Label);

            if (Widgets.ButtonInvisible(rect))
            {
                listSelectedPawnId = item.Id;
                if (_pawnById.TryGetValue(item.Id, out Pawn? pawn))
                    Find.Selector.Select(pawn, false, true);
            }

            return rect.yMax + 2f;
        }

        private static float CalcListHeight(AgentListGroups groups)
        {
            float h = 0f;
            float sectionHeaderH = RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            float rowH = 26f;

            h += sectionHeaderH + groups.Active.Count * rowH + RimMindUI.Padding;
            h += sectionHeaderH + groups.Paused.Count * rowH + RimMindUI.Padding;
            if (groups.PendingCreation.Count > 0)
                h += sectionHeaderH + groups.PendingCreation.Count * rowH;
            if (groups.Other.Count > 0)
                h += RimMindUI.Padding + sectionHeaderH + groups.Other.Count * rowH;

            return h;
        }
    }
}
