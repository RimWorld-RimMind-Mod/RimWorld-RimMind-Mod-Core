using RimMind.Domain.Agent.Policies;
using RimMind.Infrastructure.Verse;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Autonomy
        {
            public static AutonomyLevel GetLevel(Pawn? pawn, AutonomyDomain domain)
            {
                if (pawn == null) return AgentAutonomyPolicy.CreateDefault().GetLevel(domain);
                var comp = pawn.TryGetComp<CompPawnAgent>();
                if (comp?.AutonomyPolicy != null)
                {
                    return comp.AutonomyPolicy.GetLevel(domain);
                }
                return AgentAutonomyPolicy.CreateDefault().GetLevel(domain);
            }

            public static bool CanExecuteAutonomous(Pawn? pawn, AutonomyDomain domain)
            {
                return GetLevel(pawn, domain) == AutonomyLevel.FullAutonomous;
            }

            public static bool RequiresApproval(Pawn? pawn, AutonomyDomain domain)
            {
                return GetLevel(pawn, domain) == AutonomyLevel.RequireApproval;
            }

            public static bool IsDisabled(Pawn? pawn, AutonomyDomain domain)
            {
                return GetLevel(pawn, domain) == AutonomyLevel.Disabled;
            }

            public static void SetLevel(Pawn? pawn, AutonomyDomain domain, AutonomyLevel level)
            {
                if (pawn == null) return;
                var comp = pawn.TryGetComp<CompPawnAgent>();
                if (comp != null)
                {
                    comp.AutonomyPolicy.SetLevel(domain, level);
                }
            }
        }
    }
}
