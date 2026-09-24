using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models;
using RimMind.Domain.ValueObjects;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int HealthCheckIntervalMs = RimMindDefaults.Player2HealthCheckIntervalMs;
        private const int MinHealthCheckIntervalSec = RimMindDefaults.Player2MinHealthCheckIntervalSec;
        private const int LocalAvailabilityTimeoutSec = 2;
        private const int HealthCheckPollingDelayMs = RimMindDefaults.HealthCheckPollingDelayMs;

        private static DateTime _lastHealthCheck = DateTime.MinValue;
        private static volatile bool _healthCheckActive;
        private static CancellationTokenSource _healthCheckCts = new CancellationTokenSource();

        private async Task StartHealthCheckLoopAsync()
        {
            try
            {
                while (_healthCheckActive && Current.Game != null)
                {
                    await Task.Delay(HealthCheckIntervalMs, _healthCheckCts.Token);
                    if (_healthCheckActive) await EnsureHealthCheck(force: true);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logSink?.LogFromBackground($"[RimMind-Core] Player2 health check loop crashed: {ex.Message}", isWarning: true);
                _healthCheckActive = false;
            }
        }

        private void StartHealthCheckLoop()
        {
            _ = StartHealthCheckLoopAsync();
        }

        private async Task EnsureHealthCheck(bool force = false)
        {
            if (_isLocalConnection || string.IsNullOrEmpty(_apiKey)) return;
            if (!force && (DateTime.Now - _lastHealthCheck).TotalSeconds < MinHealthCheckIntervalSec) return;

            try
            {
                using var webRequest = UnityWebRequest.Get($"{CurrentApiUrl}/v1/health");
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null) return;
                    await Task.Delay(HealthCheckPollingDelayMs);
                }

                _lastHealthCheck = DateTime.Now;
                if (webRequest.responseCode != 200)
                    _logSink?.LogFromBackground(
                        $"[RimMind-Core] Player2 health check failed: {webRequest.responseCode}", isWarning: true);
            }
            catch (Exception ex)
            {
                _logSink?.LogFromBackground(
                    $"[RimMind-Core] Player2 health check exception: {ex.Message}", isWarning: true);
            }
        }

        public static void StopHealthCheck()
        {
            _healthCheckActive = false;
            _healthCheckCts.Cancel();
        }

        public static void CheckPlayer2StatusAndNotify()
        {
            Task.Run(async () =>
            {
                bool isAvailable = await IsPlayer2LocalAppAvailableAsync();
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    Messages.Message(
                        isAvailable
                            ? "RimMind.Infrastructure.Player2.LocalDetected".Translate()
                            : "RimMind.Infrastructure.Player2.LocalNotFound".Translate(),
                        isAvailable ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.CautionInput);
                });
            });
        }

        private static async Task<bool> IsPlayer2LocalAppAvailableAsync()
        {
            try
            {
                using var webRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health");
                webRequest.timeout = LocalAvailabilityTimeoutSec;
                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null) return false;
                    await Task.Delay(50);
                }
                return webRequest.responseCode == 200;
            }
            catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Core] Player2 local availability check failed: {ex.Message}"); return false; }
        }

        internal void InitiateHealthCheckIfNeeded()
        {
            if (!_healthCheckActive && !string.IsNullOrEmpty(_apiKey) && !_isLocalConnection)
            {
                if (_healthCheckCts.IsCancellationRequested)
                {
                    _healthCheckCts.Dispose();
                    _healthCheckCts = new CancellationTokenSource();
                }
                _healthCheckActive = true;
                StartHealthCheckLoop();
            }
        }
    }
}
