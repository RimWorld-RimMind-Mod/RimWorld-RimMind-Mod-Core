using RimMind.Application.Common.Interfaces.Agent.Planning;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Reflection;
using RimMind.Application.Common.Interfaces.Agent.Social;

namespace RimMind.Application.Common.Interfaces.Agent.Modes
{
    /// <summary>
    /// Interface for agent modes that support proactive behavior extensions.
    /// Extracted from ProactiveAgentMode to satisfy OCP — ProactiveBehaviorExecutor
    /// depends on this interface rather than the concrete ProactiveAgentMode.
    /// </summary>
    public interface IProactiveExtensions
    {
        IReflectionStrategy? ReflectionStrategy { get; }
        IDailyPlanner? DailyPlanner { get; }
        IPsychologyWatcher? PsychologyWatcher { get; }
        ISocialEventOrganizer? SocialEventOrganizer { get; }
        ITraitEvolutionEngine? TraitEvolutionEngine { get; }
    }
}
