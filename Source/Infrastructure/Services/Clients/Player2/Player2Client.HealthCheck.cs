using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int HealthCheckIntervalMs = RimMindDefaults.Player2HealthCheckIntervalMs;
        private const int MinHealthCheckIntervalSec = RimMindDefaults.Player2MinHealthCheckIntervalSec;
        private const int LocalAvailabilityTimeoutSec = 2;

        private static readonly object _healthCheckLock = new object();
        private static DateTime _lastHealthCheck = DateTime.MinValue;
        private static volatile bool _healthCheckActive;
        private static CancellationTokenSource? _healthCheckCts;

        private async Task StartHealthCheckLoopAsync(CancellationToken ct)
        {
            try
            {
                while (_healthCheckActive && !ct.IsCancellationRequested)
                {
                    await Task.Delay(HealthCheckIntervalMs, ct).ConfigureAwait(false);
                    if (_healthCheckActive && !ct.IsCancellationRequested)
                        await EnsureHealthCheck(force: true).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logSink?.LogFromBackground($"[RimMind-Core] Player2 health check loop crashed: {Shared.LogSanitizer.ForLog(ex.Message)}", isWarning: true);
                lock (_healthCheckLock)
                {
                    _healthCheckActive = false;
                }
            }
        }

        private void StartHealthCheckLoop(CancellationToken ct)
        {
            _ = StartHealthCheckLoopAsync(ct);
        }

        private async Task EnsureHealthCheck(bool force = false)
        {
            if (_isLocalConnection || string.IsNullOrEmpty(_apiKey) || !_healthCheckActive) return;
            if (!force && (DateTime.Now - _lastHealthCheck).TotalSeconds < MinHealthCheckIntervalSec) return;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{CurrentApiUrl}/v1/health");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
                request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

                using var response = await SharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                _lastHealthCheck = DateTime.Now;
                if ((int)response.StatusCode != 200)
                    _logSink?.LogFromBackground(
                        $"[RimMind-Core] Player2 health check failed: {(int)response.StatusCode}", isWarning: true);
            }
            catch (Exception ex)
            {
                _logSink?.LogFromBackground(
                    $"[RimMind-Core] Player2 health check exception: {Shared.LogSanitizer.ForLog(ex.Message)}", isWarning: true);
            }
        }

        public static void StopHealthCheck()
        {
            CancellationTokenSource? ctsToCancel;
            lock (_healthCheckLock)
            {
                _healthCheckActive = false;
                ctsToCancel = _healthCheckCts;
                _healthCheckCts = null;
            }

            if (ctsToCancel != null)
            {
                try { ctsToCancel.Cancel(); }
                catch (ObjectDisposedException) { }
                try { ctsToCancel.Dispose(); }
                catch (ObjectDisposedException) { }
            }
        }

        public static void CheckPlayer2StatusAndNotify()
        {
            Task.Run(async () =>
            {
                bool isAvailable = await IsPlayer2LocalAppAvailableAsync();
                RimMind.Presentation.Api.RimMindAPI.MainThread.Post(() =>
                {
                    string key = isAvailable
                        ? "RimMind.Infrastructure.Player2.LocalDetected"
                        : "RimMind.Infrastructure.Player2.LocalNotFound";
                    string message = key.CanTranslate()
                        ? (string)key.Translate()
                        : (isAvailable ? "Player2 local app detected." : "Player2 local app not found.");
                    Messages.Message(
                        message,
                        isAvailable ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.CautionInput);
                });
            });
        }

        private static async Task<bool> IsPlayer2LocalAppAvailableAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LocalAvailabilityTimeoutSec));
                using var response = await SharedHttpClient.GetAsync($"{LocalUrl}/v1/health", cts.Token).ConfigureAwait(false);
                return (int)response.StatusCode == 200;
            }
            catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Core] Player2 local availability check failed: {Shared.LogSanitizer.ForLog(ex.Message)}"); return false; }
        }

        internal void InitiateHealthCheckIfNeeded()
        {
            if (string.IsNullOrEmpty(_apiKey) || _isLocalConnection)
                return;

            CancellationToken ct;
            CancellationTokenSource? staleCts = null;
            lock (_healthCheckLock)
            {
                if (_healthCheckActive)
                    return;

                staleCts = _healthCheckCts;
                _healthCheckCts = new CancellationTokenSource();
                ct = _healthCheckCts.Token;
                _healthCheckActive = true;
            }

            if (staleCts != null)
            {
                try { staleCts.Dispose(); }
                catch (ObjectDisposedException) { }
            }

            StartHealthCheckLoop(ct);
        }
    }
}
