using System.Threading;
using System.Threading.Tasks;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Domain.Storage
{
    /// <summary>
    /// Optional remote key-value backend. Only registered when user enables cloud sync.
    /// All keys MUST use "rimmind:" prefix.
    /// </summary>
    public interface IRemoteBackend
    {
        string ProviderName { get; }
        bool IsConfigured { get; }

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<RemoteEntry?, RimMindError>> PullAsync(string key, CancellationToken ct);

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<bool, RimMindError>> PushAsync(string key, string json, long localVersion, CancellationToken ct);

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<bool, RimMindError>> DeleteAsync(string key, CancellationToken ct);
    }
}
