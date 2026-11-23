using System.Collections.Generic;
using RimMind.Application.Common.Models.Client;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IApiCredentialSettings
    {
        string Provider { get; set; }
        string ApiKey { get; set; }
        string ApiEndpoint { get; set; }
        IReadOnlyList<BackupEndpointNode>? BackupEndpoints { get; }
        bool IsConfigured { get; }
        string GetProviderOption(string key);
        void SetProviderOption(string key, string value);
    }
}
