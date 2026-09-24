using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Presentation.Settings
{
    internal sealed partial class SettingsProvider : ISettingsProvider
    {
        private readonly RimMindCoreSettings _settings;

        public SettingsProvider(RimMindCoreSettings settings)
        {
            _settings = settings;
        }

        public IContextSettings Context => _settings.Context;
    }
}
