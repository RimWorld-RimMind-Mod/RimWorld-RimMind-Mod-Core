using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Domain.ValueObjects;
using Newtonsoft.Json;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int BalanceQueryTimeoutSec = RimMindDefaults.Player2BalanceQueryTimeout;
        private const int BalancePollingDelayMs = RimMindDefaults.Player2BalancePollingDelay;

        private static volatile float _cachedJoulesBalance = -1f;
        private static DateTime _lastBalanceCheck = DateTime.MinValue;
        private static readonly object _balanceLock = new object();

        public static float CachedJoulesBalance => _cachedJoulesBalance;

        public async Task<float> GetJoulesBalanceAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return -1f;

            try
            {
                if (Current.Game == null) return -1f;
                string endpoint = $"{CurrentApiUrl}/v1/balance";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(BalanceQueryTimeoutSec));
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
                request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

                using var response = await SharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return -1f;

                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var balance = JsonConvert.DeserializeObject<Player2JoulesBalance>(text);
                return balance?.Balance ?? -1f;
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Core] GetJoulesBalanceAsync failed: {ex.Message}");
                return -1f;
            }
        }

        public static void RefreshJoulesBalance(ISettingsProvider? settingsProvider)
        {
            if (settingsProvider == null || AIProviderRegistry.RequiresApiKey(settingsProvider.Provider)) return;

            Task.Run(async () =>
            {
                var client = await CreateAsync(settingsProvider);
                if (client?.IsConfigured() == true)
                {
                    float balance = await client.GetJoulesBalanceAsync();
                    lock (_balanceLock)
                    {
                        _cachedJoulesBalance = balance;
                        _lastBalanceCheck = DateTime.Now;
                    }
                }
            });
        }
    }
}
