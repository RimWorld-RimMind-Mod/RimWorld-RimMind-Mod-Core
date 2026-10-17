namespace RimMind.Domain.Agent.Policies
{
    /// <summary>
    /// Functional domains of autonomous pawn actions in RimWorld.
    /// </summary>
    public enum AutonomyDomain
    {
        /// <summary>
        /// Emergency self-preservation (fleeing fire/threats, self-tending bleedouts, wearing parkas during extreme cold).
        /// </summary>
        EmergencySurvival = 0,

        /// <summary>
        /// Designating wild resources on map (harvesting wild berries, chopping marked wood, basic surface mining).
        /// </summary>
        ResourceMarking = 1,

        /// <summary>
        /// Autonomous creation of production bills at worktables (cooking meals at stoves, tailoring winter parkas).
        /// </summary>
        WorktableBills = 2,

        /// <summary>
        /// Placing construction blueprints (beds, walls, passive coolers, micro-room projects).
        /// </summary>
        BlueprintPlacement = 3,

        /// <summary>
        /// High-risk irreversible or costly actions (demolishing permanent structures, consuming scarce components, medical surgery).
        /// </summary>
        HighRiskOperations = 4
    }
}
