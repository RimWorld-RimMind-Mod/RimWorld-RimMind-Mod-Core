namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IApiCredentialSettings
    {
        string Provider { get; set; }
        string ApiKey { get; set; }
        string ApiEndpoint { get; set; }
        string Player2RemoteUrl { get; set; }
        bool IsConfigured { get; }
        bool IsOpenAIConfigured();
    }
}
