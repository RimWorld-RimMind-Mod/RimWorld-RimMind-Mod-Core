using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public static class JobDocs
    {
        public static MechanismDocs Value => new MechanismDocs
        {
            Summary = "Manage pawn jobs, work assignments, and job queue",
            QueryDescription = "Query the pawn's current job, detailed queued jobs list, and work status",
            SetDescription = "Assign or queue jobs on a pawn. Supports action: assign_work, move_to, eat_food, force_rest, tend_pawn, rescue_pawn, arrest_pawn, cancel_job, enqueue_job, queue_job_chain, remove_queued_job, clear_queue.",
            ListDescription = "List available job actions for pawns"
        };
    }
}
