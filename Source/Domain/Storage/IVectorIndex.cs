using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Domain.ValueObjects;

namespace RimMind.Domain.Storage
{
    /// <summary>
    /// Semantic search index. Skeleton only — implementation deferred to Phase 3.
    /// </summary>
    public interface IVectorIndex
    {
        string ProviderName { get; }
        bool IsConfigured { get; }

        Task<Result<List<MemoryHit>, RimMindError>> SearchAsync(string query, int limit, CancellationToken ct);
        Task<Result<bool, RimMindError>> IndexAsync(MemoryEntry entry, CancellationToken ct);
    }
}
