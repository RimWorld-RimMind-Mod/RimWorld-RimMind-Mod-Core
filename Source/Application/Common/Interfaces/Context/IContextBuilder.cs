using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Context;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Context building — snapshot construction and scheduler/embedding store access.
    /// </summary>
    public interface IContextBuilder
    {
        /// <summary>
        /// Builds a snapshot using async context providers when available (KeyMeta.Def != null).
        /// Falls back to synchronous providers for legacy keys; never blocks on async providers.
        /// </summary>
        Task<ContextSnapshot?> BuildSnapshotFromEnvelopeAsync(string npcId, string? currentQuery,
            int maxTokens = 800, float temperature = 0.7f, string? scenarioId = null,
            HashSet<string>? skipLayers = null,
            CancellationToken ct = default);

        IBudgetScheduler? GetScheduler();

        EmbeddingSnapshotStore? GetEmbeddingSnapshotStore();
    }
}
