using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Requests.Queue;
using Xunit;

namespace RimMind.Tests.Presentation.Settings
{
    public class OverlaySettingsTests
    {
        [Fact]
        public void OverlaySettings_PropertyMutations_PersistCorrectly()
        {
            var provider = new DefaultSettingsProvider();
            IOverlaySettings settings = provider;

            Assert.False(settings.ShowAgentProgressFloat);
            settings.RequestOverlayX = 150f;
            settings.RequestOverlayY = 250f;
            settings.RequestOverlayW = 400f;
            settings.RequestOverlayH = 200f;
            settings.RequestOverlayEnabled = false;
            settings.RequestOverlayAutoHideWhenEmpty = false;
            settings.ShowAgentProgressFloat = true;
            settings.EnableFloatingMentalMonitor = true;

            Assert.Equal(150f, settings.RequestOverlayX);
            Assert.Equal(250f, settings.RequestOverlayY);
            Assert.Equal(400f, settings.RequestOverlayW);
            Assert.Equal(200f, settings.RequestOverlayH);
            Assert.False(settings.RequestOverlayEnabled);
            Assert.False(settings.RequestOverlayAutoHideWhenEmpty);
            Assert.True(settings.ShowAgentProgressFloat);
            Assert.True(settings.EnableFloatingMentalMonitor);
        }

        [Fact]
        public void OverlaySettings_ToggleAutoHide_UpdatesState()
        {
            var provider = new DefaultSettingsProvider();
            IOverlaySettings settings = provider;

            Assert.True(settings.RequestOverlayW > 0);
            Assert.True(settings.RequestOverlayH > 0);
            Assert.True(settings.RequestOverlayEnabled);
            Assert.True(settings.RequestOverlayAutoHideWhenEmpty);
            Assert.False(settings.EnableFloatingMentalMonitor);
            settings.RequestOverlayAutoHideWhenEmpty = false;
            Assert.False(settings.RequestOverlayAutoHideWhenEmpty);
            settings.RequestOverlayAutoHideWhenEmpty = true;
            Assert.True(settings.RequestOverlayAutoHideWhenEmpty);
        }
    }
}
