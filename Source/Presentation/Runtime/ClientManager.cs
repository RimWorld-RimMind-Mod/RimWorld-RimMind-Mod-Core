using System;
using System.Linq;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Presentation.Runtime
{
    public class ClientManager : IClientManager, IDisposable
    {
        private readonly object _sync = new object();
        private IAIClient? _client;
        private string _lastProvider = "";
        private string _lastApiKey = "";
        private string _lastEndpoint = "";
        private string _lastModel = "";
        private readonly ISettingsProvider? _settingsProvider;
        private readonly IExtensionRegistry<IAIClientFactory>? _factoryRegistry;
        private bool _disposed;

        public ClientManager(ISettingsProvider? settingsProvider = null,
            IExtensionRegistry<IAIClientFactory>? factoryRegistry = null)
        {
            _settingsProvider = settingsProvider;
            _factoryRegistry = factoryRegistry;
        }

        private ISettingsProvider? GetSettings()
            => _settingsProvider;

        private IExtensionRegistry<IAIClientFactory>? GetFactoryRegistry()
            => _factoryRegistry;

        public IAIClient? GetClient()
        {
            var s = GetSettings();
            if (s == null) return null;

            string provider = s.Provider;
            string apiKey = s.ApiKey;
            string endpoint = s.ApiEndpoint;
            string model = s.ModelName;

            IAIClient? staleClient = null;
            IAIClient? currentClient;

            lock (_sync)
            {
                if (_disposed) return null;

                if (_client != null &&
                    _lastProvider == provider &&
                    _lastApiKey == apiKey &&
                    _lastEndpoint == endpoint &&
                    _lastModel == model)
                {
                    return _client;
                }

                staleClient = _client;
                _client = CreateClient(s);
                _lastProvider = provider;
                _lastApiKey = apiKey;
                _lastEndpoint = endpoint;
                _lastModel = model;
                currentClient = _client;
            }

            DisposeClient(staleClient);
            return currentClient;
        }

        public void InvalidateCache()
        {
            IAIClient? staleClient;
            lock (_sync)
            {
                staleClient = _client;
                _client = null;
            }

            DisposeClient(staleClient);
        }

        private IAIClient? CreateClient(ISettingsProvider s)
        {
            var factoryRegistry = GetFactoryRegistry();
            var factory = factoryRegistry?.All.FirstOrDefault(f => f.ProviderId == s.Provider);
            return factory?.Create(s);
        }

        private static void DisposeClient(IAIClient? client)
        {
            if (client is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* swallow dispose errors */ }
            }
        }

        public void Dispose()
        {
            IAIClient? staleClient;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                staleClient = _client;
                _client = null;
            }

            DisposeClient(staleClient);
        }
    }
}
