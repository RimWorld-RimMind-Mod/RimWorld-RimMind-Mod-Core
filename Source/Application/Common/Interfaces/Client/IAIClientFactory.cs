using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Application.Common.Interfaces.Client
{
    public interface IAIClientFactory : Extension.IExtension
    {
        string ProviderId { get; }
        bool RequiresApiKey { get; }
        IAIClient Create(ISettingsProvider settings);

        string DisplayLabel { get; }
        string? DefaultEndpoint { get; }
        string? DefaultModelName { get; }
        int OrderWeight { get; }
        bool VisibleInMenu { get; }
    }
}
