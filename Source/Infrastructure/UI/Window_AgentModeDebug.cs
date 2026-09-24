using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Events;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Api;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_AgentModeDebug : RimMindWindowBase
    {
        private Vector2 _pawnListScrollPos = Vector2.zero;
        private Vector2 _detailScrollPos = Vector2.zero;
        private Vector2 _modesScrollPos = Vector2.zero;
        private Vector2 _historyScrollPos = Vector2.zero;

        private int _selectedPawnIndex = -1;
        private int _targetModeIndex;
        private readonly RuntimeBinding _runtimeBinding = new RuntimeBinding();
        private readonly string _subscriptionKey = "AgentModeDebugWindow:" + Guid.NewGuid().ToString("N");
        private readonly List<AgentModeChangedEvent> _modeChangeHistory = new();
        private List<Pawn> _cachedPawns = new();
        private Pawn? _initialPawn;

        public override Vector2 InitialSize => new Vector2(740f, 580f);

        public Window_AgentModeDebug() : this(null) { }

        public Window_AgentModeDebug(Pawn? pawn)
        {
            _initialPawn = pawn;
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            _runtimeBinding.Refresh(BindRuntime);
            RefreshPawnCache();

            float y = RimMindUI.DrawWindowHeader(inRect, "RimMind.UI.AgentModeDebug.Title".Translate());

            // Left-right split: Pawn list on left, detail on right
            float listW = 220f;
            float detailW = inRect.width - listW - RimMindUI.Padding * 2;
            float bodyH = inRect.height - y + inRect.y;

            Rect pawnListRect = new Rect(inRect.x, y, listW, bodyH);
            Rect detailRect = new Rect(inRect.x + listW + RimMindUI.Padding, y, detailW, bodyH);
            scope.Record(pawnListRect, "List:Pawns");
            scope.Record(detailRect, "Detail:Panel");

            // Divider line between panels
            Widgets.DrawLine(
                new Vector2(inRect.x + listW + RimMindUI.Padding * 0.5f, y),
                new Vector2(inRect.x + listW + RimMindUI.Padding * 0.5f, inRect.yMax),
                RimMindUI.ColorDivider, RimMindUI.DividerThickness);

            DrawPawnList(pawnListRect, scope);
            DrawDetailPanel(detailRect, scope);
        }

        public override void PreClose()
        {
            _runtimeBinding.Dispose();
            base.PreClose();
        }

        #region Pawn List (Left Panel)

        private void DrawPawnList(Rect rect, RimMindLayoutScope scope)
        {
            // Section header
            float y = rect.y;
            y = RimMindUI.DrawSectionHeader(rect, y, "RimMind.UI.AgentModeDebug.PawnList".Translate());

            Rect listRect = new Rect(rect.x, y, rect.width, rect.height - (y - rect.y));

            if (_cachedPawns.Count == 0)
            {
                RimMindUI.DrawEmptyState(listRect, "RimMind.UI.AgentModeDebug.NoPawns".Translate(),
                    "RimMind.UI.AgentModeDebug.NoPawnsHint".Translate());
                return;
            }

            float contentH = _cachedPawns.Count * RimMindUI.LineHeight;
            Rect viewRect = new Rect(listRect.x, listRect.y, listRect.width - 16f, contentH);
            Widgets.BeginScrollView(listRect, ref _pawnListScrollPos, viewRect);
            scope.Record(listRect, "ScrollView:PawnListOuter");
            scope.Record(viewRect, "ScrollView:PawnListContent");

            float rowY = listRect.y;
            for (int i = 0; i < _cachedPawns.Count; i++)
            {
                Pawn pawn = _cachedPawns[i];
                var comp = CompPawnAgent.GetComp(pawn);
                if (comp?.Agent == null) continue;

                IAgentControl agent = comp.Agent;
                string label = $"{pawn.Name?.ToStringShort ?? pawn.LabelShort}";

                Rect rowRect = new Rect(viewRect.x, rowY, viewRect.width, RimMindUI.LineHeight);
                scope.Record(rowRect, $"PawnRow:{i}");
                if (i == _selectedPawnIndex)
                    Widgets.DrawBoxSolid(rowRect, RimMindUI.ColorTabActive);

                if (Widgets.ButtonInvisible(rowRect))
                    _selectedPawnIndex = i;

                // Pawn name
                GUI.color = i == _selectedPawnIndex ? RimMindUI.ColorHeader : RimMindUI.ColorValue;
                Widgets.Label(new Rect(rowRect.x + RimMindUI.Padding, rowRect.y, rowRect.width * 0.55f, RimMindUI.LineHeight), label);

                // Mode badge
                string modeLabel = (string)agent.CurrentModeId;
                GUI.color = i == _selectedPawnIndex ? RimMindUI.ColorActive : RimMindUI.ColorMuted;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rowRect.x + rowRect.width * 0.55f, rowRect.y, rowRect.width * 0.45f, RimMindUI.LineHeight), modeLabel);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                rowY += RimMindUI.LineHeight;
            }

            Widgets.EndScrollView();
        }

        #endregion

        #region Detail Panel (Right Panel)

        private void DrawDetailPanel(Rect rect, RimMindLayoutScope scope)
        {
            if (_selectedPawnIndex < 0 || _selectedPawnIndex >= _cachedPawns.Count)
            {
                RimMindUI.DrawEmptyState(rect, "RimMind.UI.AgentModeDebug.SelectPawn".Translate());
                return;
            }

            Pawn pawn = _cachedPawns[_selectedPawnIndex];
            var comp = CompPawnAgent.GetComp(pawn);
            if (comp?.Agent == null) return;

            IAgentControl agent = comp.Agent;
            IAgentMode currentMode = agent.CurrentMode;

            float contentH = CalculateDetailContentHeight(agent, currentMode, rect.width);
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _detailScrollPos, viewRect);
            scope.Record(rect, "ScrollView:DetailOuter");
            scope.Record(viewRect, "ScrollView:DetailContent");

            float y = viewRect.y;

            // ── Section: Agent Info ──
            y = RimMindUI.DrawSectionHeader(viewRect, y, pawn.Name?.ToStringShort ?? pawn.LabelShort);

            var (stateTextColor, stateBgColor) = RimMindUI.GetStateBadgeColors(agent.IsActive, agent.State == Domain.Enums.AgentState.Paused);
            string stateKey = $"RimMind.Agent.State.{agent.State}";
            y = RimMindUI.DrawStatusBadge(viewRect, y, stateKey.Translate(), stateTextColor, stateBgColor);

            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentModeDebug.Mode".Translate(), (string)agent.CurrentModeId);

            // ── Section: Mode Details ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentModeDebug.ModeDetails".Translate());

            if (currentMode != null)
            {
                var toolRegistry = RimMindAPI.Tools;
                IReadOnlyList<string> allowedTools = toolRegistry != null
                    ? currentMode.AllowedToolIds(toolRegistry)
                    : Array.Empty<string>();
                string toolsStr = allowedTools.Count > 0 ? string.Join(", ", allowedTools) : "-";
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentModeDebug.AllowedTools".Translate(), toolsStr);

                bool shouldThink = currentMode.ShouldThink(agent, Array.Empty<PerceptionBufferEntry>());
                string thinkLabel = shouldThink.ToString();
                Color thinkColor = shouldThink ? RimMindUI.ColorActive : RimMindUI.ColorMuted;
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentModeDebug.ShouldThink".Translate(), thinkLabel);
            }

            // ── Section: Mode Switch ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentModeDebug.SwitchTo".Translate());
            y = DrawModeSwitchButtons(viewRect, y, agent);

            // ── Section: Registered Modes ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = DrawRegisteredModesSection(viewRect, y);

            // ── Section: History ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = DrawHistorySection(viewRect, y);

            Widgets.EndScrollView();
        }

        private float DrawModeSwitchButtons(Rect viewRect, float y, IAgentControl agent)
        {
            IExtensionRegistry<IAgentMode>? modeRegistry = RimMindAPI.Modes;
            if (modeRegistry == null) return y;

            IReadOnlyList<IAgentMode> modes = modeRegistry.All;
            if (modes.Count == 0) return y;

            if (_targetModeIndex < 0 || _targetModeIndex >= modes.Count)
                _targetModeIndex = 0;

            float x = viewRect.x + RimMindUI.Padding;
            float rowY = y;
            float maxX = viewRect.x + viewRect.width - RimMindUI.Padding;

            for (int i = 0; i < modes.Count; i++)
            {
                string modeLabel = (string)modes[i].ModeId;
                float btnW = Text.CalcSize(modeLabel).x + RimMindUI.Padding * 4;

                if (x + btnW > maxX)
                {
                    x = viewRect.x + RimMindUI.Padding;
                    rowY += RimMindUI.BtnHeight + RimMindUI.Padding * 0.5f;
                }

                Rect btnRect = new Rect(x, rowY, btnW, RimMindUI.BtnHeight);
                if (i == _targetModeIndex)
                    Widgets.DrawBoxSolid(btnRect, RimMindUI.ColorTabActive);
                bool isTarget = i == _targetModeIndex;
                if (isTarget)
                {
                    Widgets.DrawAtlas(btnRect, Widgets.ButtonBGAtlasClick);
                    TextAnchor prevAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(btnRect, modeLabel);
                    Text.Anchor = prevAnchor;

                if (Widgets.ButtonText(btnRect, modeLabel))
                    _targetModeIndex = i;
                    if (Widgets.ButtonInvisible(btnRect))
                        _targetModeIndex = i;
                }
                else
                {
                    if (Widgets.ButtonText(btnRect, modeLabel))
                        _targetModeIndex = i;
                }

                x += btnW + RimMindUI.Padding * 0.5f;
            }

            rowY += RimMindUI.BtnHeight + RimMindUI.Padding;

            Rect switchBtnRect = new Rect(viewRect.x + RimMindUI.Padding, rowY, 120f, RimMindUI.BtnHeight);
            if (Widgets.ButtonText(switchBtnRect, "RimMind.UI.AgentModeDebug.SwitchMode".Translate()))
            {
                if (_targetModeIndex >= 0 && _targetModeIndex < modes.Count)
                {
                    agent.SwitchMode(modes[_targetModeIndex].ModeId);
                }
            }

            return rowY + RimMindUI.BtnHeight + RimMindUI.Padding;
        }

        private float DrawRegisteredModesSection(Rect viewRect, float y)
        {
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentModeDebug.RegisteredModes".Translate());

            IReadOnlyList<IAgentMode>? modes = RimMindAPI.Modes?.All;
            if (modes == null)
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentModeDebug.NoModes".Translate(), RimMindUI.ColorMuted);
                return y;
            }

            if (modes.Count == 0)
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentModeDebug.NoModes".Translate(), RimMindUI.ColorMuted);
                return y;
            }

            foreach (var mode in modes)
            {
                string entry = "RimMind.UI.AgentModeDebug.ModeEntry".Translate((string)mode.ModeId, mode.DisplayName);
                y = RimMindUI.DrawKeyValueRow(viewRect, y, (string)mode.ModeId, mode.DisplayName);
            }

            return y;
        }

        private float DrawHistorySection(Rect viewRect, float y)
        {
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentModeDebug.History".Translate());

            if (_modeChangeHistory.Count == 0)
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentModeDebug.NoHistory".Translate(), RimMindUI.ColorMuted);
                return y;
            }

            Text.Font = GameFont.Tiny;
            for (int i = _modeChangeHistory.Count - 1; i >= 0; i--)
            {
                AgentModeChangedEvent evt = _modeChangeHistory[i];
                string entry = "RimMind.UI.AgentModeDebug.ModeChange".Translate(
                    evt.NpcId, evt.OldMode, evt.NewMode) + $" [T:{evt.Timestamp}]";
                y = RimMindUI.DrawWrappedLabel(viewRect, y, entry, RimMindUI.ColorMuted);
            }
            Text.Font = GameFont.Small;

            return y;
        }

        private float CalculateDetailContentHeight(IAgentControl agent, IAgentMode? currentMode, float width)
        {
            float h = RimMindUI.Padding;

            // Agent Info section
            h += RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // header
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // badge
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // mode

            // Mode Details section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            if (currentMode != null)
            {
                h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 2;
            }

            // Mode Switch section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            IExtensionRegistry<IAgentMode>? modeRegistry = RimMindAPI.Modes;
            if (modeRegistry != null && modeRegistry.All.Count > 0)
            {
                h += RimMindUI.BtnHeight + RimMindUI.Padding * 0.5f; // mode buttons row (approximate)
                h += RimMindUI.BtnHeight + RimMindUI.Padding; // switch button
            }

            // Registered Modes section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            if (modeRegistry != null)
            {
                h += modeRegistry.All.Count * (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f);
            }

            // History section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            h += _modeChangeHistory.Count * (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f);

            h += RimMindUI.Padding;
            return h;
        }

        #endregion

        #region Bus Subscription

        private IDisposable? BindRuntime(RuntimeServiceScope scope)
        {
            IAgentBus? bus = scope.GetOptional<IAgentBus>();
            if (bus == null)
                return null;
            bus.Subscribe<AgentModeChangedEvent>(_subscriptionKey, OnModeChanged);
            return new BusSubscriptionLease(bus, _subscriptionKey);
        }

        private sealed class BusSubscriptionLease : IDisposable
        {
            private IAgentBus? _bus;
            private readonly string _key;

            public BusSubscriptionLease(IAgentBus bus, string key)
            {
                _bus = bus;
                _key = key;
            }

            public void Dispose()
            {
                IAgentBus? bus = _bus;
                _bus = null;
                bus?.Unsubscribe<AgentModeChangedEvent>(_key);
            }
        }

        private void OnModeChanged(AgentModeChangedEvent evt)
        {
            _modeChangeHistory.Add(evt);
            while (_modeChangeHistory.Count > MaxHistoryEntries)
                _modeChangeHistory.RemoveAt(0);
        }

        private const int MaxHistoryEntries = 20;

        #endregion

        #region Pawn Cache

        private void RefreshPawnCache()
        {
            _cachedPawns.Clear();
            var map = Find.CurrentMap;
            if (map == null) return;

            foreach (Pawn pawn in map.mapPawns.AllPawns)
            {
                var comp = CompPawnAgent.GetComp(pawn);
                if (comp?.Agent != null)
                    _cachedPawns.Add(pawn);
            }

            if (_initialPawn != null && _selectedPawnIndex < 0)
            {
                int idx = _cachedPawns.IndexOf(_initialPawn);
                if (idx >= 0)
                    _selectedPawnIndex = idx;
                _initialPawn = null;
            }
        }

        #endregion
    }
}
