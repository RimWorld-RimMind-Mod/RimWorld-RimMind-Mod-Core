using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Common;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public class Player2ClientFactory : IAIClientFactory
    {
        private readonly ILogSink? _logSink;

        public Player2ClientFactory(ILogSink? logSink = null)
        {
            _logSink = logSink;
        }

        public string Id => AIProviders.Player2;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string ProviderId => AIProviders.Player2;
        public bool RequiresApiKey => false;
        public string DisplayLabel => global::Verse.Translator.Translate("RimMind.Settings.Provider.Player2");
        public string? DefaultEndpoint => null;
        public string? DefaultModelName => null;
        public int OrderWeight => 30;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            try
            {
                var client = Player2Client.CreateAsync(settings, _logSink).GetAwaiter().GetResult();
                return client;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
