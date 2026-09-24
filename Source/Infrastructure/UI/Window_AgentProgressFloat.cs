using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_AgentProgressFloat : RimMindWindowBase
    {
        private const float Padding = 6f;
        private const float LineH = 22f;
        private const float BtnHeight = 22f;
        private const float HeaderH = 28f;
        private const float EntryH = 48f;
        private const float PhaseIndicatorW = 10f;

        private Vector2 _scrollPos = Vector2.zero;
        private readonly RuntimeBinding _runtimeBinding = new RuntimeBinding();
        private readonly RuntimeServiceRef<IRequestQueue> _requestQueue = RuntimeServiceRef<IRequestQueue>.Optional();
        private readonly RuntimeServiceRef<IScopedAgentManager> _scopedAgentManager = RuntimeServiceRef<IScopedAgentManager>.Optional();
        private long _cachedGeneration = long.MinValue;
        private int _lastRefreshTick;
        private readonly List<AgentProgressEntry> _cachedEntries = new List<AgentProgressEntry>();

        public override Vector2 InitialSize => new Vector2(340f, 420f);

        public Window_AgentProgressFloat()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        public static void TryOpen()
        {
            var settings = RimMindCoreMod.Settings;
            if (settings != null && settings.showAgentProgressFloat)
            {
                if (!Find.WindowStack.IsOpen<Window_AgentProgressFloat>())
                {
                    Find.WindowStack.Add(new Window_AgentProgressFloat());
                }
            }
        }

        public override void PostOpen()
        {
            base.PostOpen();
            SubscribeBus();
            RefreshEntries();
        }

        public override void PreClose()
        {
            UnsubscribeBus();
            base.PreClose();
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            _runtimeBinding.Refresh(BindRuntime);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float y = DrawHeader(inRect, scope);

            Rect bodyRect = new Rect(inRect.x, y, inRect.width, inRect.height - y + inRect.y);
            scope.Record(bodyRect, "Body");

            if (_cachedEntries.Count == 0)
            {
                DrawEmptyState(bodyRect);
                return;
            }

            DrawAgentList(bodyRect, scope);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - _lastRefreshTick >= 60)
            {
                RefreshEntries();
                _lastRefreshTick = now;
            }
        }

        private float DrawHeader(Rect inRect, RimMindLayoutScope scope)
        {
            float y = inRect.y;

            Rect titleRect = new Rect(inRect.x, y, inRect.width, HeaderH);
            scope.Record(titleRect, "Header:Title");
            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect,
                "RimMind.UI.AgentProgressFloat.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += HeaderH;

            var queue = _requestQueue.ValueOrDefault;
            if (queue != null)
            {
                string queueInfo = queue.IsPaused
                    ? "RimMind.UI.AgentProgressFloat.QueuePaused".Translate()
                    : "RimMind.UI.AgentProgressFloat.QueueRunning".Translate(
                        queue.ActiveRequestCount.ToString(), queue.TotalQueuedCount.ToString());
                GUI.color = queue.IsPaused ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, LineH), queueInfo);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            y += LineH + Padding;

            return y;
        }

        private void DrawEmptyState(Rect rect)
        {
            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;

            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, centerY - 20f, rect.width, LineH),
                "RimMind.UI.AgentProgressFloat.NoAgents".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(rect.x, centerY + 4f, rect.width, LineH),
                "RimMind.UI.AgentProgressFloat.NoAgentsHint".Translate());

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawAgentList(Rect rect, RimMindLayoutScope scope)
        {
            float contentH = _cachedEntries.Count * (EntryH + Padding) + Padding;
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);
            scope.Record(rect, "ScrollView:Outer");
            scope.Record(viewRect, "ScrollView:Content");

            float y = viewRect.y + Padding;
            float entryW = viewRect.width - Padding * 2;

            for (int i = 0; i < _cachedEntries.Count; i++)
            {
                var entry = _cachedEntries[i];
                Rect entryRect = new Rect(viewRect.x + Padding, y, entryW, EntryH);
                scope.Record(entryRect, $"AgentEntry:{i}");

                DrawAgentEntry(entryRect, entry, scope);

                y += EntryH + Padding;
            }

            Widgets.EndScrollView();
        }

        private void DrawAgentEntry(Rect rect, AgentProgressEntry entry, RimMindLayoutScope scope)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

            Color phaseColor = PhaseColor(entry.Phase);
            Rect indicatorRect = new Rect(rect.x, rect.y, PhaseIndicatorW, rect.height);
            Widgets.DrawBoxSolid(indicatorRect, phaseColor);

            float x = rect.x + PhaseIndicatorW + Padding;
            float labelW = rect.width - PhaseIndicatorW - Padding * 2 - 80f;

            if (entry.IsScopedAgent)
            {
                GUI.color = new Color(0.7f, 0.85f, 1f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(x, rect.y + 2f, 50f, LineH),
                    entry.ScopeType ?? "");
                GUI.color = new Color(0.85f, 0.9f, 1f);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(x + 54f, rect.y + 2f, labelW - 54f, LineH), entry.PawnLabel);
            }
            else
            {
                GUI.color = new Color(0.85f, 0.9f, 1f);
                Widgets.Label(new Rect(x, rect.y + 2f, labelW, LineH), entry.PawnLabel);
            }
            GUI.color = Color.white;

            string phaseLabel = PhaseLabel(entry.Phase);
            GUI.color = phaseColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, rect.y + LineH + 2f, labelW, LineH), phaseLabel);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (entry.ElapsedTicks > 0)
            {
                float elapsedSec = entry.ElapsedTicks / 60f;
                string elapsedStr = elapsedSec < 60f
                    ? $"{elapsedSec:F0}s"
                    : $"{elapsedSec / 60f:F1}m";
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(x, rect.y + LineH * 2 + 2f, labelW, LineH),
                    "RimMind.UI.AgentProgressFloat.Elapsed".Translate(elapsedStr));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            float btnX = rect.xMax - 76f;
            Rect detailBtn = new Rect(btnX, rect.y + (rect.height - BtnHeight) / 2f, 72f, BtnHeight);
            scope.Record(detailBtn, "Button:Details");
            if (Widgets.ButtonText(detailBtn, "RimMind.UI.AgentProgressFloat.Details".Translate()))
            {
                if (entry.IsScopedAgent && entry.AgentControl != null)
                {
                    Find.WindowStack.Add(new Window_AgentStateDebug(entry.AgentControl));
                }
                else if (entry.Pawn != null)
                {
                    Find.WindowStack.Add(new Window_AgentStateDebug(entry.Pawn));
                }
                else
                {
                    Find.WindowStack.Add(new Window_AgentStateDebug());
                }
            }
        }

        private void RefreshEntries()
        {
            _cachedEntries.Clear();

            if (Find.CurrentMap == null) return;

            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                var comp = CompPawnAgent.GetComp(pawn);
                if (comp?.Agent == null) continue;

                var phase = AgentWorkflowPhase.Idle;
                int lastThinkTick = 0;

                if (comp.Agent is IPawnAgent pawnAgent)
                {
                    phase = pawnAgent.WorkflowPhase;
                    lastThinkTick = pawnAgent.LastThinkTick ?? 0;
                }

                int elapsedTicks = 0;
                if (lastThinkTick > 0)
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    elapsedTicks = now - lastThinkTick;
                }

                _cachedEntries.Add(new AgentProgressEntry(
                    pawn,
                    pawn.Name?.ToStringShort ?? pawn.LabelShort,
                    phase,
                    elapsedTicks,
                    comp.Agent.State,
                    agentControl: comp.Agent));
            }

            var scopedAgentManager = _scopedAgentManager.ValueOrDefault;
            if (scopedAgentManager != null)
            {
                foreach (var scoped in scopedAgentManager.GetAll())
                {
                    var phase = AgentWorkflowPhase.Idle;
                    int elapsedTicks = 0;
                    int? lastThinkTick = (scoped as IAgentInfo)?.LastThinkTick;
                    if (lastThinkTick.HasValue && lastThinkTick.Value > 0)
                    {
                        int now = Find.TickManager?.TicksGame ?? 0;
                        elapsedTicks = now - lastThinkTick.Value;
                    }

                    _cachedEntries.Add(new AgentProgressEntry(
                        null,
                        scoped.Label,
                        phase,
                        elapsedTicks,
                        scoped.State,
                        scoped.ScopeType,
                        scoped));
                }
            }

            _lastRefreshTick = Find.TickManager?.TicksGame ?? 0;
        }

        private IDisposable? BindRuntime(RuntimeServiceScope scope)
        {
            if (_cachedGeneration != scope.Generation)
                _cachedEntries.Clear();
            _cachedGeneration = scope.Generation;
            IAgentBus? bus = scope.GetOptional<IAgentBus>();
            if (bus == null)
                return null;
            string subscriptionKey = bus.SubscribeByName(
                nameof(AgentBusEventType.WorkflowPhaseChange),
                OnWorkflowPhaseChange);
            return new BusSubscriptionLease(bus, subscriptionKey);
        }

        private void SubscribeBus()
        {
            _runtimeBinding.Refresh(BindRuntime);
        }

        private void UnsubscribeBus()
        {
            _runtimeBinding.Dispose();
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
                bus?.Unsubscribe<AgentBusEvent>(_key);
            }
        }

        private void OnWorkflowPhaseChange(AgentBusEvent evt)
        {
            RefreshEntries();
        }

        private static Color PhaseColor(AgentWorkflowPhase phase)
        {
            return phase switch
            {
                AgentWorkflowPhase.Idle => new Color(0.5f, 0.5f, 0.5f),
                AgentWorkflowPhase.Perceiving => new Color(0.3f, 0.7f, 1f),
                AgentWorkflowPhase.Thinking => new Color(0.4f, 1f, 0.4f),
                AgentWorkflowPhase.Acting => new Color(1f, 0.8f, 0.3f),
                AgentWorkflowPhase.Recording => new Color(0.7f, 0.5f, 1f),
                _ => Color.grey
            };
        }

        private static string PhaseLabel(AgentWorkflowPhase phase)
        {
            return phase switch
            {
                AgentWorkflowPhase.Idle => "RimMind.UI.AgentProgressFloat.PhaseIdle".Translate(),
                AgentWorkflowPhase.Perceiving => "RimMind.UI.AgentProgressFloat.PhasePerceiving".Translate(),
                AgentWorkflowPhase.Thinking => "RimMind.UI.AgentProgressFloat.PhaseThinking".Translate(),
                AgentWorkflowPhase.Acting => "RimMind.UI.AgentProgressFloat.PhaseActing".Translate(),
                AgentWorkflowPhase.Recording => "RimMind.UI.AgentProgressFloat.PhaseRecording".Translate(),
                _ => phase.ToString()
            };
        }

        private readonly struct AgentProgressEntry
        {
            public readonly Pawn? Pawn;
            public readonly string PawnLabel;
            public readonly AgentWorkflowPhase Phase;
            public readonly int ElapsedTicks;
            public readonly AgentState State;
            public readonly string? ScopeType;
            public readonly IAgentControl? AgentControl;

            public bool IsScopedAgent => Pawn == null && ScopeType != null;

            public AgentProgressEntry(Pawn? pawn, string pawnLabel, AgentWorkflowPhase phase, int elapsedTicks, AgentState state, string? scopeType = null, IAgentControl? agentControl = null)
            {
                Pawn = pawn;
                PawnLabel = pawnLabel;
                Phase = phase;
                ElapsedTicks = elapsedTicks;
                State = state;
                ScopeType = scopeType;
                AgentControl = agentControl;
            }
        }
    }
}
