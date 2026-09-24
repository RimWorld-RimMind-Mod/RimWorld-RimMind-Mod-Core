using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Application.Features.Storage;
using RimMind.Domain.Settings;
using RimMind.Domain.Storage;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime.Composition
{
    internal sealed class ClientCompositionServices
    {
        public IClientManager ClientManager { get; init; } = null!;
        public IExtensionRegistry<IAIClientFactory> ClientFactoryRegistry { get; init; } = null!;
    }

    internal sealed class RemoteSyncCompositionServices
    {
        public RemoteSyncSettings Settings { get; init; } = null!;
        public IRemoteSyncService Service { get; init; } = null!;
    }

    internal static class ClientComposition
    {
        public static ClientCompositionServices ComposeClientManager(
            RuntimeServiceBuilder services,
            ExtensionRegistryCatalog extensions,
            ISettingsProvider resolvedSettings)
        {
            var clientFactoryRegistry = extensions.GetExtensionRegistry<IAIClientFactory>();
            var clientManager = new ClientManager(resolvedSettings, clientFactoryRegistry);
            services.Bind<IClientManager>(clientManager);
            services.Bind(clientFactoryRegistry);

            return new ClientCompositionServices
            {
                ClientManager = clientManager,
                ClientFactoryRegistry = clientFactoryRegistry
            };
        }

        public static void RegisterBuiltinClientFactories(
            IExtensionRegistry<IAIClientFactory> clientFactoryRegistry,
            ILogSink logSink,
            IOpenAISettings? openAISettings)
        {
            Infrastructure.DependencyInjection.RegisterBuiltinClientFactories(
                clientFactoryRegistry,
                logSink,
                openAISettings);
        }

        public static RemoteSyncCompositionServices ComposeRemoteSync(
            RuntimeServiceBuilder services,
            ILogSink logSink,
            IRemoteBackend? remoteBackend = null)
        {
            var remoteSyncSettings = new RemoteSyncSettings();
            var remoteSyncOrchestrator = new RemoteSyncOrchestrator(remoteBackend, remoteSyncSettings, logSink);
            var remoteSyncService = new RimMind.Infrastructure.Services.Storage.RemoteSyncService(remoteSyncOrchestrator);
            services.Bind(remoteSyncSettings);
            services.Bind<IRemoteSyncService>(remoteSyncService);

            return new RemoteSyncCompositionServices
            {
                Settings = remoteSyncSettings,
                Service = remoteSyncService
            };
        }
    }
}
