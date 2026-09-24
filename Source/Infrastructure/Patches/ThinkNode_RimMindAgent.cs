using RimMind.Infrastructure.Verse;
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
            return priority > 0f ? priority : 5f;
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            var comp = pawn.GetComp<CompPawnAgent>();
            if (comp == null || comp.Agent == null || !comp.Agent.IsActive) return ThinkResult.NoJob;
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
