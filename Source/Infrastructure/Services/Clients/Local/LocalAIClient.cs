using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Services.Clients.Local
{
    /// <summary>
    /// Local AI client that wraps an existing IAIClient's legacy SendAsync(AIRequest)
    /// to provide the new unified IAIClient interface.
    /// Used for local-only operation where no remote endpoint is available.
    /// </summary>
    public class LocalAIClient : IAIClient
    {
        private readonly IAIClient _inner;

        public bool IsLocalEndpoint => true;
        public bool SupportsStreaming => false;
        public bool SupportsNpcServerState => false;

        public LocalAIClient(IAIClient inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool IsConfigured() => true;

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            return await _inner.SendAsync(envelope);
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendStreamAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope, Action<RimMind.Domain.Llm.LlmChunk> onChunk, CancellationToken ct)
        {
            var result = await SendAsync(envelope);

            if (result.IsOk)
            {
                onChunk(new RimMind.Domain.Llm.LlmChunk
                {
                    DeltaContent = result.Value.Content,
                });

                onChunk(new RimMind.Domain.Llm.LlmChunk
                {
                    IsLast = true,
                    FinalResponse = result.Value,
                });

                return result;
            }
            else
            {
                return result;
            }
        }

        public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile)
        {
            throw new NotSupportedException("LocalAIClient does not support NPC server-side state");
        }

        public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
        {
            throw new NotSupportedException("LocalAIClient does not support NPC server-side state");
        }

        public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
        {
            throw new NotSupportedException("LocalAIClient does not support NPC server-side state");
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
