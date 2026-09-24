using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IContextSettings : IContextIncludeSettings, IContextBudgetSettings, IContextEnvironmentSettings
    {
        void ApplyPreset(ContextPreset preset);
        void ResetToDefault();
    }
}
