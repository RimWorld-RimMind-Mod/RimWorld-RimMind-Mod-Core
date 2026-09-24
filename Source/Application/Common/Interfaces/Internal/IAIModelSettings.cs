namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IAIModelSettings
    {
        int MaxTokens { get; set; }
        float DefaultTemperature { get; set; }
        bool ForceJsonMode { get; set; }
        string ModelName { get; set; }
    }
}
