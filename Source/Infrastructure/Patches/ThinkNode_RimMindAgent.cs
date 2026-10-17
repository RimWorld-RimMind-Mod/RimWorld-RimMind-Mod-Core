using RimMind.Infrastructure.Verse;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Infrastructure.Patches
{
    public class ThinkNode_RimMindAgent : ThinkNode
    {
        public override float GetPriority(Pawn pawn)
        {
            var comp = pawn.GetComp<CompPawnAgent>();
            if (comp == null || comp.Agent == null || !comp.Agent.IsActive) return 0f;

            var session = comp.ActiveIntentSession;
            if (session != null)
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!session.IsActive(pawn, currentTick))
                {
                    comp.ClearIntentSession();
                }
                else
                {
                    // Yield to native emergency behaviors for acute life safety:
                    // Burning, extreme bleeding (>0.8/day), or violent mental states
                    bool isBurning = pawn.HasAttachment(ThingDefOf.Fire);
                    float bleedRate = pawn.health?.hediffSet?.BleedRateTotal ?? 0f;
                    if (pawn.InMentalState || isBurning || bleedRate > 0.8f)
                    {
                        return 0f;
                    }
                    return session.Priority;
                }
            }

            return priority > 0f ? priority : 5f;
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            var comp = pawn.GetComp<CompPawnAgent>();
            if (comp == null || comp.Agent == null || !comp.Agent.IsActive) return ThinkResult.NoJob;

            var session = comp.ActiveIntentSession;
            if (session != null)
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (session.IsActive(pawn, currentTick))
                {
                    var nextJob = session.TryIssueNextJob(pawn);
                    if (nextJob != null)
                    {
                        nextJob.jobGiver = this;
                        return new ThinkResult(nextJob, this, default, false);
                    }

                    if (session.CompletionPredicate?.Invoke(pawn) == true)
                    {
                        comp.ClearIntentSession();
                    }
                }
                else
                {
                    comp.ClearIntentSession();
                }
            }

            var job = comp.ConsumePendingJob();
            if (job == null) return ThinkResult.NoJob;
            job.jobGiver = this;
            return new ThinkResult(job, this, default, false);
        }

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            var node = (ThinkNode_RimMindAgent)base.DeepCopy(resolve);
            return node;
        }
    }
}
