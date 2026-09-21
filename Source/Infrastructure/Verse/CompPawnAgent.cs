using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
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

        private Texture2D? _agentIcon;
        private Texture2D? _agentDevIcon;

        private Pawn Pawn => (Pawn)parent;

        public override void CompTick()
        {
            base.CompTick();
            EnsureCurrentAgent();
            EnsureAgentLoopRegistration();
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
