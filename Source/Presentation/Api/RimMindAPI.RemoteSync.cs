using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Narrow lifecycle-aware remote storage capability for dependent Mods.
        /// </summary>
        public static class RemoteSync
        {
            public static bool IsConfigured
            {
                get
                {
                    var scope = RuntimeServiceHub.Shared.Capture();
                    return scope.Snapshot.State == RuntimeLifecycleState.Running
                        && scope.GetOptional<IRemoteSyncService>()?.IsConfigured == true;
                }
            }

            public static Task<Result<string?, RimMindError>> SyncOnLoadAsync(
                string key,
                long localVersion,
                CancellationToken cancellationToken = default)
            {
                var scope = RuntimeServiceHub.Shared.Capture();
                var service = CaptureRunningService(scope);
                return service == null
                    ? Task.FromResult(Result<string?, RimMindError>.Err(RuntimeUnavailable()))
                    : CompleteCurrentAsync(
                        scope.Token,
                        service.SyncOnLoadAsync(key, localVersion, cancellationToken));
            }

            public static Task<Result<bool, RimMindError>> EnqueuePushAsync(
                string key,
                string json,
                long localVersion,
                CancellationToken cancellationToken = default)
            {
                var scope = RuntimeServiceHub.Shared.Capture();
                var service = CaptureRunningService(scope);
                return service == null
                    ? Task.FromResult(Result<bool, RimMindError>.Err(RuntimeUnavailable()))
                    : CompleteCurrentAsync(
                        scope.Token,
                        service.EnqueuePushAsync(key, json, localVersion, cancellationToken));
            }

            private static IRemoteSyncService? CaptureRunningService(RuntimeServiceScope scope)
            {
                return scope.Snapshot.State == RuntimeLifecycleState.Running
                    ? scope.GetOptional<IRemoteSyncService>()
                    : null;
            }

            private static async Task<Result<T, RimMindError>> CompleteCurrentAsync<T>(
                RuntimeGenerationToken token,
                Task<Result<T, RimMindError>> operation)
            {
                var result = await operation.ConfigureAwait(false);
                if (RuntimeServiceHub.Shared.IsCurrent(token))
                {
                    return result;
                }

                RuntimeServiceHub.Shared.RecordStaleCompletion(LifecycleEventSources.RemoteSync);
                return Result<T, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("runtime generation retired"));
            }

            private static RimMindError RuntimeUnavailable() =>
                RimMindErrors.PipelineShortCircuited("runtime is not running");
        }
    }
}
