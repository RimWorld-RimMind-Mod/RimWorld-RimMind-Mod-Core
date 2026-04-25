using System.Collections.Generic;
using RimMind.Core.Agent;
using RimMind.Core.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Core.Comps
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
        public PawnAgent Agent { get; private set; } = null!;

        private Pawn Pawn => (Pawn)parent;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (Agent == null)
            {
                Agent = new PawnAgent(Pawn);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            Agent?.Tick();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            var agent = Agent;
            Scribe_Deep.Look(ref agent, "pawnAgent");
            if (agent != null) Agent = agent;

            if (Agent == null && parent is Pawn pawn)
                Agent = new PawnAgent(pawn);

            if (Agent != null && Agent.Pawn == null)
                Agent = new PawnAgent(Pawn);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Agent == null) yield break;

            string stateLabel = Agent.State.ToString();
            string toggleLabel = Agent.IsActive
                ? "RimMind.Core.Agent.Gizmo.Deactivate".Translate()
                : "RimMind.Core.Agent.Gizmo.Activate".Translate();

            yield return new Command_Action
            {
                defaultLabel = $"Agent: {stateLabel}",
                defaultDesc = "RimMind.Core.Agent.Gizmo.ToggleDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/AgentIcon", reportFailure: false),
                action = () =>
                {
                    if (Agent.IsActive)
                        Agent.TransitionTo(AgentState.Dormant);
                    else
                        Agent.TransitionTo(AgentState.Active);
                },
            };

            if (Agent.IsActive)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Core.Agent.Gizmo.Dialogue".Translate(),
                    defaultDesc = "RimMind.Core.Agent.Gizmo.DialogueDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/AgentIcon", reportFailure: false),
                    action = () =>
                    {
                        Find.WindowStack.Add(new Window_AgentDialogue(Pawn));
                    },
                };
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Core.Agent.Gizmo.DevView".Translate(),
                    defaultDesc = "RimMind.Core.Agent.Gizmo.DevViewDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/AgentIcon", reportFailure: false),
                    action = () =>
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"State: {Agent.State}");
                        sb.AppendLine($"Goals: {Agent.GoalStack.TotalCount}");
                        foreach (var g in Agent.GoalStack.Goals)
                            sb.AppendLine($"  - [{g.Status}] {g.Description} (P:{g.Priority:F1})");
                        sb.AppendLine($"Behavior History: {Agent.BehaviorHistory.Count}");
                        var topW = Agent.StrategyOptimizer.GetTopN(5);
                        if (topW.Count > 0)
                        {
                            sb.AppendLine("Strategy Weights (Top 5):");
                            foreach (var kv in topW)
                                sb.AppendLine($"  {kv.Key}: {kv.Value:F2}");
                        }
                        Log.Message($"[RimMind-Agent] {Pawn.Name?.ToStringShort}\n{sb}");
                    },
                };
            }
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
    }
}
