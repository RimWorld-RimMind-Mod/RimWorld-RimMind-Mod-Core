using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Enums;

namespace RimMind.Presentation.Settings
{
    internal sealed partial class SettingsProvider
    {
        public int ContextCalibrateInterval
        {
            get => _settings.contextCalibrateInterval;
            set => _settings.contextCalibrateInterval = value;
        }
        public int ContextDiffLifetimeTicks
        {
            get => _settings.contextDiffLifetimeTicks;
            set => _settings.contextDiffLifetimeTicks = value;
        }
    }
}
