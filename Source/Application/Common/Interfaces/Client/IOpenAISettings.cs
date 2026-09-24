namespace RimMind.Application.Common.Interfaces.Client
{
    public interface IOpenAISettings
    {
        string ApiEndpoint { get; }
        string ModelName { get; }
        string ApiKey { get; }
        bool ForceJsonMode { get; }
        int MaxTokens { get; }
        float DefaultTemperature { get; }
        bool DebugLogging { get; }
        bool IsConfigured();
    }
}
