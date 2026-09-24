using System.Text;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Aggregates streaming LlmChunk instances into a complete LlmResponse.
    /// Ensures the final chunk has IsLast=true and FinalResponse populated.
    /// </summary>
    internal sealed class ChunkAggregator
    {
        private readonly string _requestId;
        private readonly StringBuilder _contentBuilder = new();
        private readonly StringBuilder _toolCallsBuilder = new();
        private string? _reasoningContent;
        private int _promptTokens;
        private int _completionTokens;
        private int _cachedTokens;
        private int _chunkCount;
        private RimMindError? _error;

        public ChunkAggregator(string requestId)
        {
            _requestId = requestId;
        }

        public void SetError(RimMindError error)
        {
            _error = error;
        }

        public void Append(LlmChunk chunk)
        {
            _chunkCount++;
            if (chunk.DeltaContent != null)
                _contentBuilder.Append(chunk.DeltaContent);
            if (chunk.DeltaToolCallsJson != null)
                _toolCallsBuilder.Append(chunk.DeltaToolCallsJson);
            if (chunk.DeltaReasoningContent != null)
                _reasoningContent += chunk.DeltaReasoningContent;
            if (chunk.DeltaPromptTokens.HasValue)
                _promptTokens = chunk.DeltaPromptTokens.Value;
            if (chunk.DeltaCompletionTokens.HasValue)
                _completionTokens = chunk.DeltaCompletionTokens.Value;
            if (chunk.DeltaCachedTokens.HasValue)
                _cachedTokens = chunk.DeltaCachedTokens.Value;
        }

        public Result<LlmResponse, RimMindError> BuildFinalResponse()
        {
            if (_error != null)
            {
                return Result<LlmResponse, RimMindError>.Err(_error);
            }

            var response = new LlmResponse
            {
                RequestId = _requestId,
                Content = _contentBuilder.ToString(),
                ToolCallsJson = _toolCallsBuilder.Length > 0 ? _toolCallsBuilder.ToString() : null,
                ReasoningContent = _reasoningContent,
                TokensUsed = _promptTokens + _completionTokens,
                PromptTokens = _promptTokens,
                CompletionTokens = _completionTokens,
                CachedTokens = _cachedTokens,
                State = AIRequestState.Completed,
                Priority = 0,
                AttemptCount = 0,
                QueueWaitMs = 0,
                ProcessingMs = 0,
                HttpStatusCode = 0,
            };
            return Result<LlmResponse, RimMindError>.Ok(response);
        }

        /// <summary>
        /// Creates the final LlmChunk with IsLast=true and FinalResponse populated.
        /// </summary>
        public LlmChunk CreateFinalChunk()
        {
            var result = BuildFinalResponse();
            return new LlmChunk
            {
                DeltaContent = null,
                DeltaToolCallsJson = null,
                IsLast = true,
                FinalResponse = result.IsOk ? result.Value : null,
            };
        }

        public int ChunkCount => _chunkCount;
    }
}
