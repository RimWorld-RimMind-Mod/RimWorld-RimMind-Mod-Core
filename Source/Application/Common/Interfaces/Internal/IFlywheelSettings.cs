using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IFlywheelSettings
    {
        FlywheelAutoApplyMode AutoApplyMode { get; set; }
        float AutoApplyConfidenceThreshold { get; set; }
    }
}
