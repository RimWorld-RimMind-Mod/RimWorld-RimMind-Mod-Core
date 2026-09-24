using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public static class JobDocs
    {
        public static MechanismDocs Value => new MechanismDocs
        {
            Summary = "Manage pawn jobs and work assignments",
            QueryDescription = "Query the pawn's current job and work status",
            SetDescription = "Assign a job or action to a pawn. Use the 'action' field to specify which job to assign.",
            ListDescription = "List available job actions for pawns"
        };
    }
}
