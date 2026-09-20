using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using RimMind.Infrastructure.Verse;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Agent;
using UnityEngine;
using Verse;
using RimMind.Infrastructure.UI.AgentStatePreview;

namespace RimMind.Infrastructure.UI
{
    public class Window_AgentStateDebug : RimMindWindowBase
    {
        private Vector2 _scrollPos = Vector2.zero;

        private readonly AgentContextPreviewCoordinator _contextPreview = new AgentContextPreviewCoordinator();
        private Pawn? _targetPawn;
        private ScopedAgentIdentity? _targetScopedAgent;

        public override Vector2 InitialSize => new Vector2(640f, 560f);

        public Window_AgentStateDebug() : this(pawn: null, agent: null) { }

        public Window_AgentStateDebug(Pawn? pawn) : this(pawn, agent: null) { }

        public Window_AgentStateDebug(IAgentControl agent) : this(pawn: null, agent)
        {
        }

        private Window_AgentStateDebug(Pawn? pawn, IAgentControl? agent)
        {
            _targetPawn = pawn;
            if (agent != null)
            {
                if (!(agent is IScopedAgent scopedAgent))
                    throw new System.ArgumentException(
                        "Pawn agents must be opened with the Pawn constructor so they can rebind.",
                        nameof(agent));
                _targetScopedAgent = new ScopedAgentIdentity(scopedAgent.ScopeType, scopedAgent.ScopeId);
            }
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float y = RimMindUI.DrawWindowHeader(inRect, "RimMind.UI.AgentStateDebug.Title".Translate());

            Rect bodyRect = new Rect(inRect.x, y, inRect.width, inRect.height - y + inRect.y);
            scope.Record(bodyRect, "Body");

            if (_targetScopedAgent.HasValue)
            {
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                ScopedAgentIdentity identity = _targetScopedAgent.Value;
                IScopedAgent? scopedAgent = runtimeScope
                    .GetOptional<IScopedAgentManager>()
                    ?.Find(identity.ScopeType, identity.ScopeId);
                if (scopedAgent != null)
                    DrawScopedAgentDetail(bodyRect, scopedAgent, scope);
                else
                    DrawNoPawnState(bodyRect, scope);
                return;
            }

            Pawn? pawn = _targetPawn ?? Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                DrawNoPawnState(bodyRect, scope);
                return;
            }

            DrawPawnDetail(bodyRect, pawn, scope);
        }

        #region Scoped Agent Detail

        private void DrawScopedAgentDetail(Rect rect, IScopedAgent scopedAgent, RimMindLayoutScope scope)
        {
            float contentH = CalculateScopedContentHeight(scopedAgent, rect.width);
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);
            scope.Record(rect, "ScrollView:ScopedOuter");
            scope.Record(viewRect, "ScrollView:ScopedContent");

            float y = viewRect.y + RimMindUI.Padding;
            float x = viewRect.x + RimMindUI.Padding;
            float labelW = viewRect.width - RimMindUI.Padding * 2;

            // ── Section: Identity ──
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.SectionIdentity".Translate());
            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.ScopeType".Translate(), scopedAgent.ScopeType.ToString());
            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.ScopeId".Translate(), scopedAgent.ScopeId);

            if (scopedAgent.MapId.HasValue)
            {
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.MapId".Translate(), scopedAgent.MapId.Value.ToString());
            }

            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.NpcId".Translate(), scopedAgent.NpcId ?? "-");

            // ── Section: State ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.SectionState".Translate());

            string stateKey = $"RimMind.Agent.State.{scopedAgent.State}";
            string stateLabel = stateKey.Translate();
            var (stateTextColor, stateBgColor) = RimMindUI.GetStateBadgeColors(
                scopedAgent.State == AgentState.Active,
                scopedAgent.State == AgentState.Paused);
            y = RimMindUI.DrawStatusBadge(viewRect, y, stateLabel, stateTextColor, stateBgColor);

            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.Mode".Translate(), (string)scopedAgent.CurrentModeId);

            int? lastThinkTick = scopedAgent.LastThinkTick;
            if (lastThinkTick.HasValue && lastThinkTick.Value > 0)
            {
                int elapsed = Find.TickManager.TicksGame - lastThinkTick.Value;
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.LastThinkTick".Translate(), elapsed.ToString());
            }
            else
            {
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.LastThinkTick".Translate(), "RimMind.UI.AgentStateDebug.NoData".Translate());
            }

            float successRate = scopedAgent.GetRecentSuccessRate();
            string rateLabel = successRate.ToString("P0");
            Color rateColor = successRate > 0.5f ? RimMindUI.ColorActive : RimMindUI.ColorPaused;
            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.SuccessRate".Translate(), rateLabel);

            // ── Section: Behavior History ──
            var recentHistory = scopedAgent.GetRecentHistory(5);
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.RecentBehavior".Translate());

            if (recentHistory.Count > 0)
            {
                Text.Font = GameFont.Tiny;
                foreach (var record in recentHistory)
                {
                    string marker = record.Success ? "OK" : "FAIL";
                    Color markerColor = record.Success ? RimMindUI.ColorActive : RimMindUI.ColorError;
                    string recordStr = $"[{marker}] {record.Action}";
                    y = RimMindUI.DrawWrappedLabel(viewRect, y, recordStr, markerColor);
                }
                Text.Font = GameFont.Small;
            }
            else
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentStateDebug.NoBehaviorHistory".Translate(), RimMindUI.ColorMuted);
            }

            // ── Debug Info ──
            string debugInfo = scopedAgent.GetDebugInfo();
            if (!string.IsNullOrEmpty(debugInfo))
            {
                y = RimMindUI.DrawDivider(viewRect, y);
                Text.Font = GameFont.Tiny;
                y = RimMindUI.DrawWrappedLabel(viewRect, y, debugInfo, RimMindUI.ColorMuted);
                Text.Font = GameFont.Small;
            }

            // ── Action Buttons ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = DrawScopedAgentButtons(x, y, labelW, scopedAgent, scope);

            Widgets.EndScrollView();
        }

        private float CalculateScopedContentHeight(IScopedAgent scopedAgent, float width)
        {
            float h = RimMindUI.Padding;
            // Identity section
            h += RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // header
            h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 3; // scope type, id, npcId
            if (scopedAgent.MapId.HasValue) h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f;
            // State section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // divider + header
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // badge
            h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 3; // mode, lastThink, successRate
            // Behavior section
            h += RimMindUI.SectionGap * 0.5f + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            var recentHistory = scopedAgent.GetRecentHistory(5);
            if (recentHistory.Count > 0)
            {
                Text.Font = GameFont.Tiny;
                foreach (var record in recentHistory)
                {
                    string recordStr = $"[{(record.Success ? "OK" : "FAIL")}] {record.Action}";
                    h += Text.CalcHeight(recordStr, width - RimMindUI.Padding * 4) + RimMindUI.Padding * 0.5f;
                }
                Text.Font = GameFont.Small;
            }
            else
            {
                h += RimMindUI.LineHeight;
            }
            // Debug info
            string debugInfo = scopedAgent.GetDebugInfo();
            if (!string.IsNullOrEmpty(debugInfo))
            {
                h += RimMindUI.SectionGap;
                Text.Font = GameFont.Tiny;
                h += Text.CalcHeight(debugInfo, width - RimMindUI.Padding * 4) + RimMindUI.Padding * 0.5f;
                Text.Font = GameFont.Small;
            }
            // Buttons
            h += RimMindUI.SectionGap;
            h += (RimMindUI.BtnHeight + RimMindUI.Padding) * 3;
            h += RimMindUI.Padding;
            return h;
        }

        private float DrawScopedAgentButtons(float x, float y, float labelW, IScopedAgent scopedAgent, RimMindLayoutScope scope)
        {
            float btnW = 160f;

            Rect forceThinkBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(forceThinkBtn, "Button:ForceThink");
            if (Widgets.ButtonText(forceThinkBtn, "RimMind.UI.AgentStateDebug.SendTestRequest".Translate()))
            {
                scopedAgent.ForceThink();
            }

            Rect requestLogBtn = new Rect(x + btnW + RimMindUI.Padding, y, btnW, RimMindUI.BtnHeight);
            scope.Record(requestLogBtn, "Button:RequestLog");
            if (Widgets.ButtonText(requestLogBtn, "RimMind.UI.AgentStateDebug.OpenRequestLog".Translate()))
            {
                Find.WindowStack.Add(new Window_RequestLog());
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            Rect toolCallBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(toolCallBtn, "Button:ToolCallDebug");
            if (Widgets.ButtonText(toolCallBtn, "RimMind.UI.AgentStateDebug.OpenToolCallDebug".Translate()))
            {
                Find.WindowStack.Add(new Window_ToolCallDebug());
            }

            Rect mechanismBtn = new Rect(x + btnW + RimMindUI.Padding, y, btnW, RimMindUI.BtnHeight);
            scope.Record(mechanismBtn, "Button:MechanismStatus");
            if (Widgets.ButtonText(mechanismBtn, "RimMind.UI.AgentStateDebug.OpenMechanismStatus".Translate()))
            {
                Find.WindowStack.Add(new Window_MechanismStatus());
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            Rect destroyBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(destroyBtn, "Button:DestroyScopedAgent");
            if (Widgets.ButtonText(destroyBtn, "RimMind.UI.AgentStateDebug.DestroyScopedAgent".Translate()))
            {
                var manager = RuntimeServiceHub.Shared.Capture().GetOptional<IScopedAgentManager>();
                if (manager != null)
                {
                    manager.Remove(scopedAgent.ScopeType, scopedAgent.ScopeId);
                    Close();
                }
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            return y;
        }

        #endregion

        #region No Pawn State

        private void DrawNoPawnState(Rect rect, RimMindLayoutScope scope)
        {
            var queue = RuntimeServiceHub.Shared.Capture().GetOptional<IRequestQueue>();
            string? queueInfo = null;
            if (queue != null)
            {
                queueInfo = queue.IsPaused
                    ? "RimMind.UI.AgentStateDebug.QueuePaused".Translate()
                    : "RimMind.UI.AgentStateDebug.QueueRunning".Translate(
                        queue.ActiveRequestCount.ToString(), queue.TotalQueuedCount.ToString());
            }
            scope.Record(rect, "EmptyState:NoPawn");
            RimMindUI.DrawEmptyState(rect, "RimMind.UI.AgentStateDebug.NoPawn".Translate(), queueInfo);
        }

        #endregion

        #region Pawn Detail

        private void DrawPawnDetail(Rect rect, Pawn pawn, RimMindLayoutScope scope)
        {
            CompleteContextSnapshotBuild();
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            IPawnAgentVerse? agent = CompPawnAgent.GetComp(pawn)?.ResolveCurrentAgent(runtimeScope);
            float contentH = CalculatePawnContentHeight(agent, rect.width);
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);
            scope.Record(rect, "ScrollView:PawnOuter");
            scope.Record(viewRect, "ScrollView:PawnContent");

            float y = viewRect.y + RimMindUI.Padding;

            // ── Section: Pawn Info ──
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.SectionPawnInfo".Translate());

            string pawnLabel = pawn.Name?.ToStringShort ?? pawn.LabelShort;
            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.PawnInfo".Translate(pawnLabel, pawn.thingIDNumber), "");

            string npcId = $"NPC-{pawn.thingIDNumber}";
            y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.NpcId".Translate(), npcId);

            // ── Section: Agent State ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.SectionAgentState".Translate());

            if (agent != null)
            {
                var (stateTextColor, stateBgColor) = RimMindUI.GetStateBadgeColors(
                    agent.State == AgentState.Active,
                    agent.State == AgentState.Paused);
                string stateKey = $"RimMind.Agent.State.{agent.State}";
                string stateLabel = stateKey.Translate();
                y = RimMindUI.DrawStatusBadge(viewRect, y, stateLabel, stateTextColor, stateBgColor);

                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.Mode".Translate(), (string)agent.CurrentModeId);

                bool isThinking = agent.WorkflowPhase == AgentWorkflowPhase.Thinking;
                string thinkingLabel = isThinking.ToString();
                Color thinkingColor = isThinking ? RimMindUI.ColorActive : RimMindUI.ColorMuted;
                y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.Thinking".Translate(), thinkingLabel);

                int? lastThinkTick = agent.LastThinkTick;
                if (lastThinkTick.HasValue && lastThinkTick.Value > 0)
                {
                    int elapsed = Find.TickManager.TicksGame - lastThinkTick.Value;
                    y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.LastThinkTick".Translate(), elapsed.ToString());
                }
                else
                {
                    y = RimMindUI.DrawKeyValueRow(viewRect, y, "RimMind.UI.AgentStateDebug.LastThinkTick".Translate(), "RimMind.UI.AgentStateDebug.NoData".Translate());
                }
            }
            else
            {
                y = RimMindUI.DrawStatusBadge(viewRect, y,
                    "RimMind.UI.AgentStateDebug.AgentMissing".Translate(),
                    RimMindUI.ColorError, new Color(0.35f, 0.15f, 0.1f, 0.6f));

                Text.Font = GameFont.Tiny;
                y = RimMindUI.DrawWrappedLabel(viewRect, y,
                    "RimMind.UI.AgentStateDebug.AgentMissingHint".Translate(), RimMindUI.ColorMuted);
                Text.Font = GameFont.Small;
            }

            // ── Section: Queue State ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.QueueState".Translate());

            var queue = RuntimeServiceHub.Shared.Capture().GetOptional<IRequestQueue>();
            if (queue != null)
            {
                string qSummary = $"Paused={queue.IsPaused} Active={queue.ActiveRequestCount} Queued={queue.TotalQueuedCount}";
                y = RimMindUI.DrawWrappedLabel(viewRect, y, qSummary, RimMindUI.ColorMuted);
            }
            else
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentStateDebug.NoData".Translate(), RimMindUI.ColorMuted);
            }

            // ── Section: Context Snapshot ──
            y = RimMindUI.DrawDivider(viewRect, y);
            y = RimMindUI.DrawSectionHeader(viewRect, y, "RimMind.UI.AgentStateDebug.ContextSnapshot".Translate());

            if (!_contextPreview.Summary.NullOrEmpty())
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, _contextPreview.Summary, RimMindUI.ColorMuted);
            }
            else
            {
                y = RimMindUI.DrawWrappedLabel(viewRect, y, "RimMind.UI.AgentStateDebug.NoData".Translate(), RimMindUI.ColorMuted);
            }

            // ── Action Buttons ──
            y = RimMindUI.DrawDivider(viewRect, y);
            float x = viewRect.x + RimMindUI.Padding;
            float labelW = viewRect.width - RimMindUI.Padding * 2;
            y = DrawButtons(x, y, labelW, pawn, scope);

            Widgets.EndScrollView();
        }

        private float CalculatePawnContentHeight(IPawnAgentVerse? agent, float width)
        {
            float h = RimMindUI.Padding;

            // Pawn Info section
            h += RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // header
            h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 2; // pawn info, npcId

            // Agent State section
            h += RimMindUI.SectionGap + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f; // divider + header
            if (agent != null)
            {
                h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // badge
                h += (RimMindUI.LineHeight + RimMindUI.Padding * 0.5f) * 3; // mode, thinking, lastThink
            }
            else
            {
                h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // badge
                h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f; // hint
            }

            // Queue section
            h += RimMindUI.SectionGap + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            h += RimMindUI.LineHeight + RimMindUI.Padding * 0.5f;

            // Context snapshot section
            h += RimMindUI.SectionGap + RimMindUI.LineHeight + RimMindUI.SectionGap * 0.5f;
            if (!_contextPreview.Summary.NullOrEmpty())
            {
                h += Text.CalcHeight(_contextPreview.Summary, width - RimMindUI.Padding * 4) + RimMindUI.Padding * 0.5f;
            }
            else
            {
                h += RimMindUI.LineHeight;
            }

            // Buttons
            h += RimMindUI.SectionGap;
            h += (RimMindUI.BtnHeight + RimMindUI.Padding) * 3;
            h += RimMindUI.Padding;
            return h;
        }

        private float DrawButtons(float x, float y, float labelW, Pawn pawn, RimMindLayoutScope scope)
        {
            float btnW = 160f;

            Rect createAgentBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(createAgentBtn, "Button:CreateAgent");
            if (Widgets.ButtonText(createAgentBtn, "RimMind.UI.AgentStateDebug.CreateAgent".Translate()))
            {
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                var factory = runtimeScope.GetOptional<IPawnAgentFactoryVerse>();
                var agentBus = runtimeScope.GetOptional<IAgentBus>();
                if (factory != null && agentBus != null)
                {
                    var createdAgent = factory.Create(pawn, agentBus);
                    if (createdAgent != null)
                    {
                        var comp = CompPawnAgent.GetComp(pawn);
                        if (comp != null && comp.Agent == null)
                            comp.Agent = createdAgent as IPawnAgentVerse;
                    }
                }
            }

            Rect buildContextBtn = new Rect(x + btnW + RimMindUI.Padding, y, btnW, RimMindUI.BtnHeight);
            scope.Record(buildContextBtn, "Button:BuildContext");
            if (Widgets.ButtonText(buildContextBtn, "RimMind.UI.AgentStateDebug.BuildContext".Translate()))
            {
                string npcId = $"NPC-{pawn.thingIDNumber}";
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                var contextEngine = runtimeScope.GetOptional<IContextBuilder>();
                if (contextEngine != null)
                {
                    _contextPreview.Begin(
                        contextEngine.BuildSnapshotFromEnvelopeAsync(npcId, "[Debug] AgentStateDebug"),
                        "RimMind.UI.ContextPreview.Loading".Translate(),
                        runtimeScope.Token);
                }
                else
                {
                    _contextPreview.MarkUnavailable("RimMind.UI.AgentStateDebug.NoData".Translate());
                }
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            Rect testThinkBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(testThinkBtn, "Button:SendTestRequest");
            if (Widgets.ButtonText(testThinkBtn, "RimMind.UI.AgentStateDebug.SendTestRequest".Translate()))
            {
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                CompPawnAgent.GetComp(pawn)?.ResolveCurrentAgent(runtimeScope)?.ForceThink();
            }

            Rect requestLogBtn = new Rect(x + btnW + RimMindUI.Padding, y, btnW, RimMindUI.BtnHeight);
            scope.Record(requestLogBtn, "Button:RequestLog");
            if (Widgets.ButtonText(requestLogBtn, "RimMind.UI.AgentStateDebug.OpenRequestLog".Translate()))
            {
                Find.WindowStack.Add(new Window_RequestLog());
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            Rect toolCallBtn = new Rect(x, y, btnW, RimMindUI.BtnHeight);
            scope.Record(toolCallBtn, "Button:ToolCallDebug");
            if (Widgets.ButtonText(toolCallBtn, "RimMind.UI.AgentStateDebug.OpenToolCallDebug".Translate()))
            {
                Find.WindowStack.Add(new Window_ToolCallDebug());
            }

            Rect mechanismBtn = new Rect(x + btnW + RimMindUI.Padding, y, btnW, RimMindUI.BtnHeight);
            scope.Record(mechanismBtn, "Button:MechanismStatus");
            if (Widgets.ButtonText(mechanismBtn, "RimMind.UI.AgentStateDebug.OpenMechanismStatus".Translate()))
            {
                Find.WindowStack.Add(new Window_MechanismStatus());
            }
            y += RimMindUI.BtnHeight + RimMindUI.Padding;

            return y;
        }

        private void CompleteContextSnapshotBuild()
        {
            _contextPreview.Poll(
                "RimMind.UI.AgentStateDebug.NoData".Translate(),
                FormatContextSnapshot);
        }

        private static string FormatContextSnapshot(ContextSnapshot snapshot)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tokens: {snapshot.EstimatedTokens}");
            sb.AppendLine($"L0={snapshot.Meta.L0Tokens} L1={snapshot.Meta.L1Tokens} L2={snapshot.Meta.L2Tokens} L3={snapshot.Meta.L3Tokens} L4={snapshot.Meta.L4Tokens}");
            sb.AppendLine($"Messages: {snapshot.Messages.Count}");
            return sb.ToString();
        }

        #endregion

        private readonly struct ScopedAgentIdentity
        {
            public ScopedAgentIdentity(string scopeType, string scopeId)
            {
                ScopeType = scopeType;
                ScopeId = scopeId;
            }

            public string ScopeType { get; }
            public string ScopeId { get; }
        }
    }
}
