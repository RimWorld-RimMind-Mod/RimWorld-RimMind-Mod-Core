using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Npc;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Client
{
    public interface IAIClient : IDisposable
    {
        // === Unified API ===
        bool IsLocalEndpoint { get; }
        bool IsConfigured();
        bool SupportsStreaming { get; }
        bool SupportsNpcServerState { get; }

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope);

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<LlmResponse, RimMindError>> SendStreamAsync(LlmRequestEnvelope envelope, Action<LlmChunk> onChunk, CancellationToken ct = default);

        // === NPC server-side state management ===
        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile);

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<bool, RimMindError>> KillNpcAsync(string npcId);

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit);
    }
}
