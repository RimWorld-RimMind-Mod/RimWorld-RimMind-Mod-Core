namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Composite interface aggregating all agent concerns.
    /// Inherit from a sub-interface when only a subset of functionality is needed:
    ///   <see cref="IAgentState"/>     — read-only state queries
    ///   <see cref="IAgentLifecycle"/> — lifecycle control (tick, transition, cleanup)
    ///   <see cref="IAgentBehavior"/>  — behavior recording
    ///   <see cref="IAgentInfo"/>      — identity and basic info
    ///   <see cref="IJobProvider"/>    — pending job consumption
    /// </summary>
    public interface IAgentControl : IAgentState, IAgentLifecycle, IAgentBehavior, IAgentInfo, IJobProvider
    {
    }
}
