using System;
using System.Linq;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Helpers;

namespace RimMind.Presentation.Runtime
{
    public class ClientManager : IClientManager, IDisposable
    {
        private IAIClient? _client;
        private IAIClient? _player2Client;
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

            if (_client != null &&
                _lastProvider == s.Provider &&
                _lastApiKey == s.ApiKey &&
                _lastEndpoint == s.ApiEndpoint &&
                _lastModel == s.ModelName)
                return _client;

            DisposeClient(ref _client);
            _client = CreateClient(s);
            _lastProvider = s.Provider;
            _lastApiKey = s.ApiKey;
            _lastEndpoint = s.ApiEndpoint;
            _lastModel = s.ModelName;
            return _client;
        }

        public IAIClient? GetPlayer2Client()
        {
            var s = GetSettings();
            if (s == null) return null;
            if (AIProviderRegistry.RequiresApiKey(s.Provider)) return null;

            if (_player2Client != null) return _player2Client;
            _player2Client = CreateClient(s);
            return _player2Client;
        }

        public void InvalidateCache()
        {
            DisposeClient(ref _client);
            DisposeClient(ref _player2Client);
        }

        private IAIClient? CreateClient(ISettingsProvider s)
        {
            var factoryRegistry = GetFactoryRegistry();
            var factory = factoryRegistry?.All.FirstOrDefault(f => f.ProviderId == s.Provider);
            return factory?.Create(s);
        }

        private static void DisposeClient(ref IAIClient? client)
        {
            if (client is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* swallow dispose errors */ }
            }
            client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeClient(ref _client);
            DisposeClient(ref _player2Client);
        }
    }
}
