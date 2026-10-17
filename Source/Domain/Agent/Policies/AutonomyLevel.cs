namespace RimMind.Domain.Agent.Policies
{
    /// <summary>
    /// Tri-state governance level for an agent autonomy domain.
    /// </summary>
    public enum AutonomyLevel
    {
        /// <summary>
        /// Disabled / Vanilla passive: pawn never self-initiates actions in this domain; follows vanilla worktab only.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Require approval: pawn formulates proposal intent; presented to player for leader approval before execution.
        /// </summary>
        RequireApproval = 1,

        /// <summary>
        /// Full autonomous: pawn initiates and executes directly without prompt, bound only by physical resource safety guards.
        /// </summary>
        FullAutonomous = 2
    }
}
