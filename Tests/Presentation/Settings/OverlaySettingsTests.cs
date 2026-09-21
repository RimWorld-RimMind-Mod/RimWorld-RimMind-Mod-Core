using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Requests.Queue;
using Xunit;

namespace RimMind.Tests.Presentation.Settings
{
    public class OverlaySettingsTests
    {
        [Fact]
        public void DefaultSettingsProvider_ShowAgentProgressFloat_DefaultsToFalse()
        {
            var provider = new DefaultSettingsProvider();
            IOverlaySettings settings = provider;

            Assert.False(settings.ShowAgentProgressFloat);
        }

        [Fact]
        public void DefaultSettingsProvider_OverlayDimensions_AreValid()
        {
            var provider = new DefaultSettingsProvider();
            IOverlaySettings settings = provider;

            Assert.True(settings.RequestOverlayW > 0);
            Assert.True(settings.RequestOverlayH > 0);
            Assert.True(settings.RequestOverlayEnabled);
            Assert.True(settings.RequestOverlayAutoHideWhenEmpty);
            Assert.False(settings.EnableFloatingMentalMonitor);
        }
    }
}
