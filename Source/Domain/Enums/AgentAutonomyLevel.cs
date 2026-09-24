namespace RimMind.Domain.Enums
{
    /// <summary>
    /// Controls how much autonomy an agent has in executing decisions.
    /// <list type="bullet">
    ///   <item><c>Manual</c> — Agent only thinks; every decision requires player approval.</item>
    ///   <item><c>Guided</c> — Low/Medium risk actions auto-execute; High/Critical require approval.</item>
    ///   <item><c>Autonomous</c> — All actions auto-execute except Critical risk.</item>
    ///   <item><c>Full</c> — All actions auto-execute without approval.</item>
    /// </list>
    /// </summary>
    public enum AgentAutonomyLevel
    {
        Manual,
        Guided,
        Autonomous,
        Full
    }
}
