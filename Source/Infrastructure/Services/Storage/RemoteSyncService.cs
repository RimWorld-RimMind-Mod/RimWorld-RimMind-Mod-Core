using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Application.Features.Storage;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Services.Storage
{
    /// <summary>
    /// Infrastructure implementation of IRemoteSyncService.
    /// Delegates to the internal RemoteSyncOrchestrator.
    /// </summary>
    public sealed class RemoteSyncService : IRemoteSyncService
    {
        private readonly RemoteSyncOrchestrator _orchestrator;

        public RemoteSyncService(RemoteSyncOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public bool IsConfigured => _orchestrator.IsConfigured;

        public Task<Result<string?, RimMindError>> SyncOnLoadAsync(string key, long localVersion, CancellationToken ct = default)
            => _orchestrator.SyncOnLoadAsync(key, localVersion, ct);

        public Task<Result<bool, RimMindError>> EnqueuePushAsync(string key, string json, long localVersion, CancellationToken ct = default)
            => _orchestrator.EnqueuePushAsync(key, json, localVersion, ct);

        public Task<Result<RemoteEntry?, RimMindError>> ManualPullAsync(string key, CancellationToken ct = default)
            => _orchestrator.ManualPullAsync(key, ct);

        public Task<Result<bool, RimMindError>> ManualPushAsync(string key, string json, long localVersion, CancellationToken ct = default)
            => _orchestrator.ManualPushAsync(key, json, localVersion, ct);
    }
}
