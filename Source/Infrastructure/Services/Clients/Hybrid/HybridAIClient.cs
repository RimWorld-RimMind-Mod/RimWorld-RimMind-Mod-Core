using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Services.Clients.Hybrid
{
    /// <summary>
    /// Hybrid AI client: remote-first with local fallback on retryable errors.
    /// Replaces the old HybridStorageDriver concept at the request layer.
    /// </summary>
    public sealed class HybridAIClient : IAIClient
    {
        private readonly IAIClient _remote;
        private readonly IAIClient _local;
        private readonly ILogSink? _logSink;

        public HybridAIClient(IAIClient remote, IAIClient local, ILogSink? logSink = null)
        {
            _remote = remote ?? throw new ArgumentNullException(nameof(remote));
            _local = local ?? throw new ArgumentNullException(nameof(local));
            _logSink = logSink;
        }

        public bool IsLocalEndpoint => false;
        public bool IsConfigured() => _remote.IsConfigured() || _local.IsConfigured();
        public bool SupportsStreaming => _remote.SupportsStreaming;
        public bool SupportsNpcServerState => _remote.SupportsNpcServerState;

        public async Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
        {
            if (!_remote.IsConfigured())
                return await _local.SendAsync(envelope);

            var result = await _remote.SendAsync(envelope);
            if (result.IsErr && IsRetryable(result.Error.Code))
            {
                _logSink?.Message($"[HybridAIClient] Remote failed ({result.Error.Code}), falling back to local");
                return await _local.SendAsync(envelope);
            }
            return result;
        }

        public async Task<Result<LlmResponse, RimMindError>> SendStreamAsync(LlmRequestEnvelope envelope, Action<LlmChunk> onChunk, CancellationToken ct = default)
        {
            if (!_remote.IsConfigured() || !_remote.SupportsStreaming)
            {
                return await _local.SendStreamAsync(envelope, onChunk, ct);
            }

            // Fallback is only safe before the remote emitted anything: once deltas reached the consumer,
            // replaying the request on the local client would duplicate (or contradict) already-shown text.
            // So we fail over pre-first-chunk only and otherwise surface the remote error as-is.
            bool remoteEmitted = false;
            var result = await _remote.SendStreamAsync(envelope, chunk =>
            {
                remoteEmitted = true;
                onChunk(chunk);
            }, ct);
            if (result.IsErr && IsRetryable(result.Error.Code) && !remoteEmitted && !ct.IsCancellationRequested)
            {
                _logSink?.Message($"[HybridAIClient] Remote streaming failed ({result.Error.Code}), falling back to local");
                return await _local.SendStreamAsync(envelope, onChunk, ct);
            }
            return result;
        }

        public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile) => _remote.SpawnNpcAsync(profile);
        public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId) => _remote.KillNpcAsync(npcId);
        public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit) => _remote.QueryNpcMemoriesAsync(npcId, query, limit);

        public void Dispose()
        {
            _remote.Dispose();
            _local.Dispose();
        }

        private static bool IsRetryable(RimMindErrorCode code)
        {
            return code == RimMindErrorCode.ClientTransientFailure
                || code == RimMindErrorCode.ClientCircuitOpen
                || code == RimMindErrorCode.Timeout;
        }
    }
}
