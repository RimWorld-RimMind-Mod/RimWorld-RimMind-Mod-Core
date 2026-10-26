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
        private long _cachedServicesGeneration = -1;
        private IPawnAgentFactoryVerse? _cachedFactory;
        private IAgentBus? _cachedAgentBus;
        private IAgentLoopScheduler? _cachedScheduler;
        private IAgentTickSettings? _cachedTickSettings;

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
            if (parent == null || !parent.IsHashIntervalTick(60))
                return;

            if (!(parent is Pawn pawn) || !RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                UnregisterFromAgentLoop();
                if (_agent != null)
                {
                    _agent.Destroy();
                    _agent = null;
                }
                return;
            }

            EnsureServicesCached();
            EnsureCurrentAgent();
            EnsureAgentLoopRegistration();

            var tickSettings = _cachedTickSettings;
            if (tickSettings?.AutoActivateColonistAgents == true
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

            if (!(parent is Pawn pawn) || !RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
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

            EnsureServicesCached();
            var scheduler = _cachedScheduler;
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

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            UnregisterFromAgentLoop();
            ClearIntentSession();
            base.PostDeSpawn(map, mode);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            if (parent is Pawn pawn)
            {
                RimMind.Presentation.Api.RimMindPawnLookup.UncachePawn(pawn);
            }
            UnregisterFromAgentLoop();
            ClearIntentSession();
            if (_agent != null)
            {
                _agent.Destroy();
                _agent = null;
            }
            base.Notify_Killed(prevMap, dinfo);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (parent is Pawn pawn)
            {
                RimMind.Presentation.Api.RimMindPawnLookup.UncachePawn(pawn);
            }
            UnregisterFromAgentLoop();
            ClearIntentSession();
            if (_agent != null)
            {
                _agent.Destroy();
                _agent = null;
            }
            base.PostDestroy(mode, previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            bool skipSavingAgent = Scribe.mode == LoadSaveMode.Saving
                && (_agent == null || !(parent is Pawn p) || !RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(p));
            if (!skipSavingAgent)
            {
                EnsureServicesCached();
                var factory = _cachedFactory;
                if (factory != null)
                {
                    IPawnAgent? pawnAgent = Agent;
                    factory.SerializeAgent(ref pawnAgent, "pawnAgent");
                    Agent = pawnAgent as IPawnAgentVerse;
                    _agentRuntimeGeneration = _cachedServicesGeneration;
                }
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
            if (!RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
                return false;
            var comp = GetComp(pawn);
            return comp?.Agent?.IsActive == true;
        }

        public bool EnsureAgentCreated()
        {
            EnsureCurrentAgent();
            return Agent != null;
        }

        private void EnsureServicesCached()
        {
            long currentGeneration = RuntimeServiceHub.Shared.Generation;
            if (_cachedServicesGeneration == currentGeneration)
                return;

            var scope = RuntimeServiceHub.Shared.Capture();
            _cachedFactory = scope.GetOptional<IPawnAgentFactoryVerse>();
            _cachedAgentBus = scope.GetOptional<IAgentBus>();
            _cachedScheduler = scope.GetOptional<IAgentLoopScheduler>();
            _cachedTickSettings = scope.GetOptional<IAgentTickSettings>();
            _cachedServicesGeneration = scope.Generation;
        }

        private void EnsureCurrentAgent()
        {
            EnsureServicesCached();
            EnsureCurrentAgent(_cachedFactory, _cachedAgentBus, _cachedServicesGeneration);
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
            EnsureCurrentAgent(factory, agentBus, scope.Generation);
        }

        private void EnsureCurrentAgent(IPawnAgentFactoryVerse? factory, IAgentBus? agentBus, long generation)
        {
            if (!(parent is Pawn pawn) || !RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                if (Agent != null)
                {
                    UnregisterFromAgentLoop();
                    Agent.Destroy();
                    Agent = null;
                }
                return;
            }

            if (factory == null || agentBus == null) return;
            if (Agent != null && _agentRuntimeGeneration == generation) return;

            if (Agent != null)
            {
                UnregisterFromAgentLoop();
                Agent.Destroy();
                Agent = null;
            }

            Agent = factory.Create(pawn, agentBus) as IPawnAgentVerse;
            _agentRuntimeGeneration = generation;
        }

    }
}
