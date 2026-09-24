using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.Settings;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Storage
{
    /// <summary>
    /// Orchestrates remote KV sync with last-write-wins strategy.
    /// User controls AutoPull/AutoPush via RemoteSyncSettings.
    /// Push is debounced to avoid excessive writes.
    /// </summary>
    public sealed class RemoteSyncOrchestrator
    {
        private readonly IRemoteBackend? _backend;
        private readonly RemoteSyncSettings _settings;
        private readonly ILogSink? _log;

        private long _lastPushTicks;
        private readonly object _debounceLock = new object();

        public RemoteSyncOrchestrator(IRemoteBackend? backend, RemoteSyncSettings settings, ILogSink? log = null)
        {
            _backend = backend;
            _settings = settings;
            _log = log;
        }

        public bool IsConfigured => _backend?.IsConfigured == true;

        /// <summary>
        /// Pull remote data if AutoPull is enabled and remote is newer.
        /// </summary>
        public async Task<Result<string?, RimMindError>> SyncOnLoadAsync(string key, long localVersion, CancellationToken ct)
        {
            if (_backend == null || !_settings.AutoPull)
                return Result<string?, RimMindError>.Ok(null);

            if (!RemoteKeys.IsValid(key))
                return Result<string?, RimMindError>.Err(RimMindErrors.Internal($"Invalid remote key: {key}"));

            try
            {
                var pullResult = await _backend.PullAsync(key, ct);
                if (pullResult.IsErr)
                    return Result<string?, RimMindError>.Err(pullResult.Error);

                var remote = pullResult.Value;
                if (remote == null)
                    return Result<string?, RimMindError>.Ok(null);

                // Last-write-wins: remote version higher → overwrite local
                if (remote.Version > localVersion)
                {
                    _log?.Message($"[RemoteSync] Pulling {key}: remote v{remote.Version} > local v{localVersion}");
                    return Result<string?, RimMindError>.Ok(remote.Json);
                }

                return Result<string?, RimMindError>.Ok(null);
            }
            catch (OperationCanceledException)
            {
                return Result<string?, RimMindError>.Err(RimMindErrors.Cancelled());
            }
            catch (Exception ex)
            {
                _log?.Warning($"[RemoteSync] Pull failed for {key}: {ex.Message}");
                return Result<string?, RimMindError>.Ok(null); // Local-first: don't fail on remote errors
            }
        }

        /// <summary>
        /// Enqueue a debounced push. Only pushes if AutoPush is enabled
        /// and debounce interval has elapsed.
        /// </summary>
        public async Task<Result<bool, RimMindError>> EnqueuePushAsync(string key, string json, long localVersion, CancellationToken ct)
        {
            if (_backend == null || !_settings.AutoPush)
                return Result<bool, RimMindError>.Ok(false);

            if (!RemoteKeys.IsValid(key))
                return Result<bool, RimMindError>.Err(RimMindErrors.Internal($"Invalid remote key: {key}"));

            // Debounce check
            var now = DateTime.UtcNow.Ticks;
            var debounceTicks = TimeSpan.FromSeconds(_settings.PushDebounceSeconds).Ticks;
            lock (_debounceLock)
            {
                if (now - _lastPushTicks < debounceTicks)
                    return Result<bool, RimMindError>.Ok(false); // Debounced
                _lastPushTicks = now;
            }

            try
            {
                var result = await _backend.PushAsync(key, json, localVersion, ct);
                if (result.IsOk && result.Value)
                    _log?.Message($"[RemoteSync] Pushed {key} v{localVersion}");
                return result;
            }
            catch (OperationCanceledException)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.Cancelled());
            }
            catch (Exception ex)
            {
                _log?.Warning($"[RemoteSync] Push failed for {key}: {ex.Message}");
                return Result<bool, RimMindError>.Ok(false); // Local-first: don't fail on remote errors
            }
        }

        /// <summary>
        /// Manual pull — always executes regardless of AutoPull setting.
        /// </summary>
        public async Task<Result<RemoteEntry?, RimMindError>> ManualPullAsync(string key, CancellationToken ct)
        {
            if (_backend == null)
                return Result<RemoteEntry?, RimMindError>.Err(RimMindErrors.Internal("No remote backend configured"));

            if (!RemoteKeys.IsValid(key))
                return Result<RemoteEntry?, RimMindError>.Err(RimMindErrors.Internal($"Invalid remote key: {key}"));

            return await _backend.PullAsync(key, ct);
        }

        /// <summary>
        /// Manual push — always executes regardless of AutoPush setting.
        /// </summary>
        public async Task<Result<bool, RimMindError>> ManualPushAsync(string key, string json, long localVersion, CancellationToken ct)
        {
            if (_backend == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.Internal("No remote backend configured"));

            if (!RemoteKeys.IsValid(key))
                return Result<bool, RimMindError>.Err(RimMindErrors.Internal($"Invalid remote key: {key}"));

            return await _backend.PushAsync(key, json, localVersion, ct);
        }
    }
}
