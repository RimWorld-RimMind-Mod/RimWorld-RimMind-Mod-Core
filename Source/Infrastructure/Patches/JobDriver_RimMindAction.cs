using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Events;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.Verse;
using RimMind.Application.Common.Interfaces.Internal;
using Verse;
using Verse.AI;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.Patches
{
    public class JobDriver_RimMindAction : JobDriver
    {
        private string ActionId => job.def.defName == "RimMind_GenericAction" ? (job.GetTarget(TargetIndex.A).Thing as Pawn)?.LabelShortCap ?? "unknown" : job.def.defName;
        private string EventId => job.loadID.ToString("x8");

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var initToil = ToilMaker.MakeToil();
            initToil.initAction = () =>
            {
                var comp = pawn.GetComp<CompPawnAgent>();
                if (comp?.Agent == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var scope = RuntimeServiceHub.Shared.Capture();
                var bridgeAccessor = scope.GetOptional<IAgentActionBridgeAccessor>();
                var agentBus = scope.GetOptional<IAgentBus>();
                var bridge = bridgeAccessor?.Current;
                if (bridge == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var targetPawn = TargetThingA as Pawn;
                string actionId = job.def.defName == "RimMind_GenericAction"
                    ? TargetThingB?.ThingID ?? "unknown"
                    : job.def.defName.Replace("RimMind_", "").ToLowerInvariant();

                bool executed = false;
                try
                {
                    bridge.Execute(pawn, actionId, targetPawn?.LabelShortCap);
                    executed = true;
                }
                catch (System.Exception ex)
                {
                    RimMindErrors.Warn($"[RimMind-Core] JobDriver_RimMindAction bridge error: {ex.Message}");
                }

                if (!executed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                agentBus?.Publish(new DecisionEvent(
                    $"NPC-{pawn.thingIDNumber}",
                    pawn.thingIDNumber,
                    "job_driven",
                    $"JobDriver executed: {actionId}",
                    actionId));
            };
            initToil.defaultCompleteMode = ToilCompleteMode.Instant;
            initToil.atomicWithPrevious = true;
            yield return initToil;

            var waitToil = ToilMaker.MakeToil();
            waitToil.defaultCompleteMode = ToilCompleteMode.Delay;
            waitToil.defaultDuration = 30;
            waitToil.WithEffect(() => DefDatabase<EffecterDef>.GetNamed("Construction"), TargetIndex.None);
            yield return waitToil;

            var finishToil = ToilMaker.MakeToil();
            finishToil.initAction = () =>
            {
                var comp = pawn.GetComp<CompPawnAgent>();
                comp?.Agent?.RecordBehavior(new BehaviorRecordDto
                {
                    Action = job.def.defName,
                    Reason = "JobDriver completed",
                    Success = true,
                    ResultReason = "Completed via JobDriver",
                    GoalProgressDelta = RimMindDefaults.GoalProgressDelta,
                    Timestamp = Find.TickManager.TicksGame,
                    ActionEventId = EventId,
                });
            };
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finishToil;
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            AddFailCondition(() => pawn.Downed || pawn.Dead);
            AddFailCondition(() => TargetThingA != null && TargetThingA.Destroyed);
        }
    }
}
