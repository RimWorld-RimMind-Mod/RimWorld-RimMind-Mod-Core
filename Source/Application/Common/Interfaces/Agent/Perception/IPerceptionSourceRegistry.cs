using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Interfaces.Agent.Perception
{
    /// <summary>
    /// Registry for perception sources. Extends IExtensionRegistry with perception-specific queries.
    /// </summary>
    public interface IPerceptionSourceRegistry : IExtensionRegistry<IPerceptionSource>
    {
    }
}
