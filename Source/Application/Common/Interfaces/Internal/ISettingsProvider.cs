using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface ISettingsProvider : IQueueSettings, IAgentTickSettings, IAIModelSettings,
        IApiCredentialSettings, ICircuitBreakerSettings, IContextCalibrationSettings,
        IDebugSettings, IOverlaySettings, IPromptSettings, IFlywheelSettings,
        IContextSettings
    {
        IContextSettings Context { get; }
    }
}
