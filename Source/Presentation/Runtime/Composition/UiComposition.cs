using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Sensor;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Settings;
using RimMind.Presentation.Sensor;
using RimMind.Presentation.Settings;
using RimMind.Presentation.UI;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime.Composition
{
    internal sealed class UiCompositionServices
    {
        public ISensorManager SensorManager { get; init; } = null!;
        public IOverlayService OverlayService { get; init; } = null!;
    }

    internal static class UiComposition
    {
        public static UiCompositionServices ComposeServices(
            RuntimeServiceBuilder services,
            ExtensionRegistryCatalog extensions)
        {
            var sensorManager = new SensorManager();
            services.Bind<ISensorManager>(sensorManager);

            var overlayService = new OverlayService();
            services.Bind<IOverlayService>(overlayService);

            services.Bind(extensions.GetExtensionRegistry<ISettingsTab>());

            return new UiCompositionServices
            {
                SensorManager = sensorManager,
                OverlayService = overlayService
            };
        }

        public static void RegisterRemoteSyncSettingsTab(
            IExtensionRegistry<ISettingsTab> settingsTabs,
            RemoteSyncSettings settings,
            IRemoteSyncService service)
        {
            settingsTabs.Register(new RemoteSyncSettingsUI(settings, service));
        }

    }
}
