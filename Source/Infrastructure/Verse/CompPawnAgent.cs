using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Agent;
using UnityEngine;
using Verse;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.Verse
{
    public class CompProperties_PawnAgent : CompProperties
    {
        public CompProperties_PawnAgent()
        {
            compClass = typeof(CompPawnAgent);
        }
    }

    public class CompPawnAgent : ThingComp
    {
        private IPawnAgentVerse? _agent;
        private IAgentLoopScheduler? _registeredLoopScheduler;
        private string? _registeredLoopKey;
        private int? _registeredPawnId;
        private long? _registeredLoopGeneration;
        private long? _registeredHubGeneration;
        private long _agentRuntimeGeneration = -1;

        public IPawnAgentVerse? Agent
        {
            get => _agent;
            internal set
            {
                if (ReferenceEquals(_agent, value))
                    return;

                UnregisterFromAgentLoop();
                _agent = value;
                EnsureAgentLoopRegistration();
            }
        }

        public AgentAutonomyPolicy AutonomyPolicy { get; } = AgentAutonomyPolicy.CreateDefault();

        private Texture2D? _agentIcon;
        private Texture2D? _agentDevIcon;

        private Pawn Pawn => (Pawn)parent;

        public override void CompTick()
        {
            base.CompTick();
            EnsureCurrentAgent();
            EnsureAgentLoopRegistration();

            var tickSettings = RuntimeServiceHub.Shared.Capture().GetOptional<IAgentTickSettings>();
            if (tickSettings?.AutoActivateColonistAgents == true
                && Pawn.IsColonist && !Pawn.Dead && Pawn.Map != null
                && _agent != null && _agent.State == AgentState.Dormant)
            {
                _agent.TransitionTo(AgentState.Active);
            }
        }

        private void EnsureAgentLoopRegistration()
        {
            if (_agent == null || _agent.State == AgentState.Terminated)
            {
                UnregisterFromAgentLoop();
                return;
            }

            if (!(parent is Pawn pawn))
            {
                UnregisterFromAgentLoop();
                return;
            }

            // Fast path: avoid allocating Scope if already registered with the current hub and scheduler generation
            if (_registeredLoopScheduler != null
                && _registeredPawnId == pawn.thingIDNumber
                && _registeredHubGeneration == RuntimeServiceHub.Shared.Generation
                && _registeredLoopGeneration == _registeredLoopScheduler.Generation)
            {
                return;
            }

            var scheduler = RuntimeServiceHub.Shared.Capture().GetOptional<IAgentLoopScheduler>();
            if (scheduler == null)
            {
                UnregisterFromAgentLoop();
                return;
            }

            var schedulerGeneration = scheduler.Generation;
            if (ReferenceEquals(_registeredLoopScheduler, scheduler)
                && _registeredPawnId == pawn.thingIDNumber
                && _registeredLoopGeneration == schedulerGeneration)
            {
                return;
            }

            UnregisterFromAgentLoop();

            var pawnId = pawn.thingIDNumber;
            var loopKey = AgentLoopKeys.ForPawn(pawnId);
            var ownsRegistration = scheduler.Register(loopKey, AgentLoopKind.Pawn, _agent)
                || ReferenceEquals(scheduler.Find(loopKey), _agent);
            if (!ownsRegistration)
                return;

            if (scheduler.Generation != schedulerGeneration)
                return;

            _registeredLoopScheduler = scheduler;
            _registeredLoopKey = loopKey;
            _registeredPawnId = pawnId;
            _registeredLoopGeneration = schedulerGeneration;
            _registeredHubGeneration = RuntimeServiceHub.Shared.Generation;
        }

        private void UnregisterFromAgentLoop()
        {
            var scheduler = _registeredLoopScheduler;
            var loopKey = _registeredLoopKey;
            if (scheduler != null && loopKey != null)
                scheduler.Unregister(loopKey);

            _registeredLoopScheduler = null;
            _registeredLoopKey = null;
            _registeredPawnId = null;
            _registeredLoopGeneration = null;
            _registeredHubGeneration = null;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            UnregisterFromAgentLoop();
            base.PostDestroy(mode, previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            var scope = RuntimeServiceHub.Shared.Capture();
            var factory = scope.GetOptional<IPawnAgentFactoryVerse>();
            if (factory != null)
            {
                IPawnAgent? pawnAgent = Agent;
                factory.SerializeAgent(ref pawnAgent, "pawnAgent");
                Agent = pawnAgent as IPawnAgentVerse;
                _agentRuntimeGeneration = scope.Generation;
            }

            if (Agent != null && !Agent.IsPawnValid)
            {
                Agent.Destroy();
                Agent = null;
            }

            var survivalLevel = AutonomyPolicy.GetLevel(AutonomyDomain.EmergencySurvival);
            var resourceLevel = AutonomyPolicy.GetLevel(AutonomyDomain.ResourceMarking);
            var billsLevel = AutonomyPolicy.GetLevel(AutonomyDomain.WorktableBills);
            var blueprintLevel = AutonomyPolicy.GetLevel(AutonomyDomain.BlueprintPlacement);
            var highRiskLevel = AutonomyPolicy.GetLevel(AutonomyDomain.HighRiskOperations);

            Scribe_Values.Look(ref survivalLevel, "autonomySurvival", AutonomyLevel.FullAutonomous);
            Scribe_Values.Look(ref resourceLevel, "autonomyResource", AutonomyLevel.FullAutonomous);
            Scribe_Values.Look(ref billsLevel, "autonomyBills", AutonomyLevel.RequireApproval);
            Scribe_Values.Look(ref blueprintLevel, "autonomyBlueprints", AutonomyLevel.RequireApproval);
            Scribe_Values.Look(ref highRiskLevel, "autonomyHighRisk", AutonomyLevel.RequireApproval);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                AutonomyPolicy.SetLevel(AutonomyDomain.EmergencySurvival, survivalLevel);
                AutonomyPolicy.SetLevel(AutonomyDomain.ResourceMarking, resourceLevel);
                AutonomyPolicy.SetLevel(AutonomyDomain.WorktableBills, billsLevel);
                AutonomyPolicy.SetLevel(AutonomyDomain.BlueprintPlacement, blueprintLevel);
                AutonomyPolicy.SetLevel(AutonomyDomain.HighRiskOperations, highRiskLevel);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                defaultLabel = "RimMind.Agent.Gizmo.Control".Translate(),
                defaultDesc = "RimMind.Agent.Gizmo.ControlDesc".Translate(),
                icon = AgentIcon,
                action = () =>
                {
                    Find.WindowStack.Add(Window_RimMindHub.OpenAgentsForPawn(Pawn));
                },
            };

            if (Prefs.DevMode && Agent != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Agent.Gizmo.DevView".Translate(),
                    defaultDesc = "RimMind.Agent.Gizmo.DevViewDesc".Translate(),
                    icon = AgentDevIcon,
                    action = () =>
                    {
                        Log.Message($"[RimMind-Core] {Pawn.Name?.ToStringShort}\n{Agent.GetDebugInfo()}");
                    },
                };
            }
        }

        private Texture2D AgentIcon =>
            _agentIcon ??= ContentFinder<Texture2D>.Get("UI/AgentIcon", reportFailure: false) ?? BaseContent.BadTex;

        private Texture2D AgentDevIcon =>
            _agentDevIcon ??= ContentFinder<Texture2D>.Get("UI/AgentDevIcon", reportFailure: false) ?? BaseContent.BadTex;

        private AgentIntentSession? _localIntentSession;

        public AgentIntentSession? ActiveIntentSession => Agent?.ActiveIntentSession ?? _localIntentSession;

        public void StartIntentSession(AgentIntentSession session)
        {
            if (Agent != null)
            {
                Agent.StartIntentSession(session);
            }
            _localIntentSession = session;
        }

        public void ClearIntentSession()
        {
            Agent?.ClearIntentSession();
            _localIntentSession = null;
        }

        public bool HasActiveIntentSession
        {
            get
            {
                var session = ActiveIntentSession;
                if (session == null) return false;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!session.IsActive(Pawn, currentTick))
                {
                    ClearIntentSession();
                    return false;
                }
                return true;
            }
        }

        public global::Verse.AI.Job? ConsumePendingJob()
        {
            return Agent?.ConsumePendingJob();
        }

        public static CompPawnAgent? GetComp(Pawn pawn)
        {
            return pawn?.GetComp<CompPawnAgent>();
        }

        public static bool IsAgentActive(Pawn pawn)
        {
            var comp = GetComp(pawn);
            return comp?.Agent?.IsActive == true;
        }

        public bool EnsureAgentCreated()
        {
            EnsureCurrentAgent();
            return Agent != null;
        }

        private void EnsureCurrentAgent()
        {
            var scope = RuntimeServiceHub.Shared.Capture();
            EnsureCurrentAgent(scope);
        }

        internal IPawnAgentVerse? ResolveCurrentAgent(RuntimeServiceScope scope)
        {
            if (scope == null || !RuntimeServiceHub.Shared.IsCurrent(scope.Token))
                return null;

            EnsureCurrentAgent(scope);
            return Agent;
        }

        private void EnsureCurrentAgent(RuntimeServiceScope scope)
        {
            var factory = scope.GetOptional<IPawnAgentFactoryVerse>();
            var agentBus = scope.GetOptional<IAgentBus>();
            if (factory == null || agentBus == null) return;
            if (Agent != null && _agentRuntimeGeneration == scope.Generation) return;

            if (Agent != null)
            {
                UnregisterFromAgentLoop();
                Agent.Destroy();
                Agent = null;
            }

            Agent = factory.Create(Pawn, agentBus) as IPawnAgentVerse;
            _agentRuntimeGeneration = scope.Generation;
        }

    }
}
