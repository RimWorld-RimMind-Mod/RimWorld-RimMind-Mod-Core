namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IContextEnvironmentSettings
    {
        int EnvironmentScanRadius { get; }
        int EnvironmentMaxItems { get; }
        float ThreatThresholdHigh { get; }
        float ThreatThresholdMedium { get; }
        float ThreatThresholdLow { get; }
        float MoodDiffThreshold { get; }
        float TemperatureDiffThreshold { get; }
    }
}
