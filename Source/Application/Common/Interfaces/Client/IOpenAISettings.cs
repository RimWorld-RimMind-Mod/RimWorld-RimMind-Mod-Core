using System.Collections.Generic;
using RimMind.Application.Common.Models.Client;

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
        IReadOnlyList<BackupEndpointNode>? BackupEndpoints { get; }
        bool IsConfigured();
    }
}
