using System.Collections.Generic;
using RimMind.Application.Common.Models.Debug;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IAIRequestTraceLog
    {
        long Revision { get; }
        IReadOnlyList<AIRequestTraceEntry> Entries { get; }
        void StartRequest(string requestId, string source, string model, string systemPrompt, string userPrompt, string assistantPrompt);
        void UpdateRequestMetadata(string requestId, string traceId, string rolloutId, int turnIndex, string modId, string scenarioId, string? npcId, bool isAgentRollout);
        void UpdateRequestPrompts(string requestId, string systemPrompt, string userPrompt, string assistantPrompt);
        void UpdateRequestLayers(string requestId, IReadOnlyList<AIRequestLayerTrace> layers);
        void CompleteRequest(string requestId, string response, int tokensUsed, int elapsedMs);
        void CompleteRequestDetailed(string requestId, string response, string? reasoningContent, int tokensUsed, int promptTokens, int completionTokens, int cachedTokens, int elapsedMs);
        void FailRequest(string requestId, string error);
        void FailRequest(string requestId, string error, int elapsedMs);
        void AddToolCall(string requestId, string toolCallId, string toolName, bool succeeded, string? error);
        void AddToolCallDetailed(string requestId, string toolCallId, string toolName, bool succeeded, string? error, string? argumentsJson, string? resultContent);
        void RecordActionOutcome(string requestId, string actionIntent, bool succeeded, string? detail = null, string? paramPreview = null);
        void Clear();
    }
}
