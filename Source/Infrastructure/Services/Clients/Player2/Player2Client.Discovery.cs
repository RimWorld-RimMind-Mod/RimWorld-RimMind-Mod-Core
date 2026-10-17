using System;
using System.Text;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.ValueObjects;
using Newtonsoft.Json;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int LocalHealthTimeoutSec = 2;
        private const int LocalLoginTimeoutSec = 3;

        private static string? _cachedLocalKey;
        private static DateTime _lastProbeUtc = DateTime.MinValue;
        private static readonly TimeSpan ProbeCooldown = TimeSpan.FromSeconds(60);
        private static Task<string?>? _activeProbeTask;
        private static readonly object _probeLock = new object();

        public static void ResetDiscoveryCache()
        {
            lock (_probeLock)
            {
                _cachedLocalKey = null;
                _lastProbeUtc = DateTime.MinValue;
                _activeProbeTask = null;
            }
        }

        public static Task<string?> ProbeLocalPlayer2KeyAsync(ILogSink? logSink)
        {
            lock (_probeLock)
            {
                if (!string.IsNullOrEmpty(_cachedLocalKey))
                    return Task.FromResult<string?>(_cachedLocalKey);

                if (DateTime.UtcNow - _lastProbeUtc < ProbeCooldown)
                    return Task.FromResult<string?>(null);

                if (_activeProbeTask != null && !_activeProbeTask.IsCompleted)
                    return _activeProbeTask;

                _activeProbeTask = Task.Run(async () =>
                {
                    try
                    {
                        var key = await TryGetLocalPlayer2Key(logSink).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(key))
                        {
                            _cachedLocalKey = key;
                            ShowNotification("RimMind.Infrastructure.Player2.LocalDetected");
                        }
                        else
                        {
                            _lastProbeUtc = DateTime.UtcNow;
                        }
                        return key;
                    }
                    catch (Exception ex)
                    {
                        _lastProbeUtc = DateTime.UtcNow;
                        logSink?.LogFromBackground($"[RimMind-Core] Player2 probe error: {ex.Message}");
                        return null;
                    }
                });

                return _activeProbeTask;
            }
        }

        public static void TriggerBackgroundDiscovery(Player2Client client, ISettingsProvider settings, ILogSink? logSink)
        {
            var task = ProbeLocalPlayer2KeyAsync(logSink);
            if (task.IsCompleted)
            {
                if (!string.IsNullOrEmpty(task.Result))
                {
                    client.UpdateDiscoveredKey(task.Result!, isLocal: true);
                }
            }
            else
            {
                task.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && !string.IsNullOrEmpty(t.Result))
                    {
                        client.UpdateDiscoveredKey(t.Result!, isLocal: true);
                    }
                }, TaskScheduler.Default);
            }
        }

        private static async Task<string?> TryGetLocalPlayer2Key(ILogSink? logSink)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(LocalHealthTimeoutSec));
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(LocalHealthTimeoutSec) };

                // 1. Health probe
                System.Net.Http.HttpResponseMessage healthResp;
                try
                {
                    healthResp = await client.GetAsync($"{LocalUrl}/v1/health", cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }

                if (!healthResp.IsSuccessStatusCode)
                    return null;

                // 2. Login probe
                using var loginCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(LocalLoginTimeoutSec));
                using var loginContent = new System.Net.Http.StringContent("{}", Encoding.UTF8, "application/json");
                var loginResp = await client.PostAsync($"{LocalUrl}/v1/login/web/{GameClientId}", loginContent, loginCts.Token).ConfigureAwait(false);
                if (!loginResp.IsSuccessStatusCode)
                    return null;

                string body = await loginResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<Player2LocalLoginResponse>(body);
                if (response != null && !string.IsNullOrEmpty(response.P2Key))
                {
                    logSink?.LogFromBackground("[RimMind-Core] Player2 local app authenticated successfully.");
                    return response.P2Key;
                }
                return null;
            }
            catch (Exception ex)
            {
                logSink?.LogFromBackground($"[RimMind-Core] Local Player2 detection failed: {ex.Message}");
                return null;
            }
        }

        private static void ShowNotification(string messageKey)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    string text = messageKey.CanTranslate() ? messageKey.Translate() : messageKey;
                    Messages.Message(text,
                        messageKey.Contains("LocalDetected")
                            ? MessageTypeDefOf.PositiveEvent
                            : MessageTypeDefOf.CautionInput);
                }
                catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Core] Failed to show notification: {ex.Message}"); }
            });
        }
    }
}
