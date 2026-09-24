using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    internal sealed class Player2LifecycleService : IPlayer2Lifecycle
    {
        private readonly ISettingsProvider? _settingsProvider;

        public Player2LifecycleService(ISettingsProvider? settingsProvider = null)
        {
            _settingsProvider = settingsProvider;
        }

        public void StopHealthCheck()
        {
            Player2Client.StopHealthCheck();
        }

        public void CheckStatusAndNotify()
        {
            Player2Client.CheckPlayer2StatusAndNotify();
        }

        public void RefreshBalance()
        {
            Player2Client.RefreshJoulesBalance(_settingsProvider);
        }

        public float CachedBalance => Player2Client.CachedJoulesBalance;
        public bool IsAvailable => Player2Client.CachedJoulesBalance >= 0;
    }
}
