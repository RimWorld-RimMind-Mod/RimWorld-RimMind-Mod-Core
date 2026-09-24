using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.UI.AgentFlow;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public partial class Window_AgentFlowLab : RimMindWindowBase
    {
        private Vector2 _scrollPos = Vector2.zero;
        private const float Padding = 6f;
        private const float LineH = 22f;
        private const float BtnHeight = 24f;
        private const float SectionGap = 10f;

        private Pawn? _selectedPawn;
        private IAgentControl? _agent;
        private IScopedAgent? _scopedAgent;
        private ContextSnapshot? _lastSnapshot;
        private readonly AgentFlowAsyncCoordinator _asyncCoordinator = new();
        private readonly AgentFlowGenerationState _generationState = new();
        private readonly AgentFlowStepTracker _stepTracker = new();
        private string _requestStatus = "";
        private RuntimeGenerationToken? _liveRequestToken;
        private int? _liveRequestTargetGeneration;
        private string _lastError = "";
        private string _lastDecisionInfo = "";
        private string _mappedMechanismsInfo = "";
        private string _queueInfo = "";
        private Pawn? _initialPawn;

        private bool _offlineMode = true;
        private bool _dryRunCompleted;
        private AgentDecision? _lastDecision;
        private MechanismWriteArgs? _lastWriteArgs;
        private MechanismOperationType _lastOperationType;
        private AgentFlowScope _selectedScope = AgentFlowScope.Pawn;
        private int _targetGeneration;
        private string _dryRunResult = "";
        private string _parsedDecisionInfo = "";
        private string _validationInfo = "";

        public override Vector2 InitialSize => new Vector2(780f, 620f);

        public Window_AgentFlowLab() : this(null) { }

        public Window_AgentFlowLab(Pawn? pawn)
        {
            _initialPawn = pawn;
            _selectedPawn = pawn;
            _lastOperationType = MechanismOperationType.Set;
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
            _stepTracker.Reset();
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            CompleteStaleLiveRequest();
            CompleteMechanismExecution();
            RefreshGenerationState();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float contentH = CalcTotalContentHeight();
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentH);
            Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);
            scope.Record(inRect, "ScrollView:FlowLabOuter");
            scope.Record(viewRect, "ScrollView:FlowLabContent");

            float y = 0f;
            float w = viewRect.width;

            Rect titleRect = new Rect(0f, y, w, LineH + 4f);
            scope.Record(titleRect, "Header:Title");
            DrawSectionHeader(ref y, w, "RimMind.UI.AgentFlowLab.Title");
            DrawOfflineModeToggle(ref y, w);
            DrawScopeSelector(ref y, w);
            DrawPawnSelection(ref y, w);
            DrawAgentLifecycle(ref y, w);
            DrawContextBuilding(ref y, w);
            DrawLlmRequest(ref y, w);
            DrawDecisionParsing(ref y, w);
            DrawMechanismMapping(ref y, w);
            DrawQueueState(ref y, w);
            DrawOpenLogs(ref y, w);
            DrawErrorLog(ref y, w);

            Widgets.EndScrollView();
        }
    }
}
