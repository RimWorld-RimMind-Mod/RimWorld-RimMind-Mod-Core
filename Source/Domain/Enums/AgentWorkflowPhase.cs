namespace RimMind.Domain.Enums
{
    /// <summary>
    /// Workflow phase for the Agent Perceive→Think→Act→Record cycle.
    /// At any given tick, the agent is in exactly one phase.
    /// Transitions: Idle → Thinking → Acting → Recording → Idle
    /// </summary>
    public enum AgentWorkflowPhase
    {
        /// <summary>Waiting for the next Think cycle.</summary>
        Idle,
        /// <summary>Collecting perceptions before thinking.</summary>
        Perceiving,
        /// <summary>AI request is in progress.</summary>
        Thinking,
        /// <summary>Executing the decision.</summary>
        Acting,
        /// <summary>Recording behavior outcome.</summary>
        Recording
    }
}
