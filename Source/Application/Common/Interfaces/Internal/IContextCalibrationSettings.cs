namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IContextCalibrationSettings
    {
        int ContextCalibrateInterval { get; set; }
        int ContextDiffLifetimeTicks { get; set; }
    }
}
