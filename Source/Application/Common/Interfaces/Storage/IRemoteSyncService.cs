using System.Threading;
using System.Threading.Tasks;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Storage
{
    /// <summary>
    /// Public API for remote KV sync. Submodules use this instead of IStorageDriver.
    /// K-phase: replaces IStorageDriver's KV responsibilities.
    /// </summary>
    public interface IRemoteSyncService
    {
        bool IsConfigured { get; }

        /// <summary>
        /// Pull remote data if available and newer than local version.
        /// Returns null if no remote data or remote is older.
        /// </summary>
        Task<Result<string?, RimMindError>> SyncOnLoadAsync(string key, long localVersion, CancellationToken ct = default);

        /// <summary>
        /// Enqueue a debounced push. Only pushes if AutoPush is enabled.
        /// </summary>
        Task<Result<bool, RimMindError>> EnqueuePushAsync(string key, string json, long localVersion, CancellationToken ct = default);

        /// <summary>
        /// Manual pull — always executes regardless of AutoPull setting.
        /// </summary>
        Task<Result<RemoteEntry?, RimMindError>> ManualPullAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Manual push — always executes regardless of AutoPush setting.
        /// </summary>
        Task<Result<bool, RimMindError>> ManualPushAsync(string key, string json, long localVersion, CancellationToken ct = default);
    }
}
