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
                RimMindErrors.Warn($"[RimMind-Core] GetJoulesBalanceAsync failed: {Shared.LogSanitizer.ForLog(ex.Message)}");
                return -1f;
            }
        }

        public static void RefreshJoulesBalance(ISettingsProvider? settingsProvider)
        {
            if (settingsProvider == null || AIProviderRegistry.RequiresApiKey(settingsProvider.Provider)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var client = _activeInstance;
                    if (client == null || !client.IsConfigured())
                    {
                        client = Create(settingsProvider);
                        if (!client.IsConfigured())
                        {
                            string? localKey = await ProbeLocalPlayer2KeyAsync(logSink: null).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(localKey))
                            {
                                client.UpdateDiscoveredKey(localKey!, isLocal: true);
                            }
                        }
                    }

                    if (client.IsConfigured())
                    {
                        float balance = await client.GetJoulesBalanceAsync().ConfigureAwait(false);
                        lock (_balanceLock)
                        {
                            _cachedJoulesBalance = balance;
                            _lastBalanceCheck = DateTime.Now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimMindErrors.Warn($"[RimMind-Core] RefreshJoulesBalance failed: {Shared.LogSanitizer.ForLog(ex.Message)}");
                }
            });
        }
    }
}
