using System.Collections.Generic;
using RimMind.Application.Common.Models.Debug;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IAIRequestTraceLog
    {
        long Revision { get; }
        IReadOnlyList<AIRequestTraceEntry> Entries { get; }
        void StartRequest(string requestId, string source, string model, string systemPrompt, string userPrompt, string assistantPrompt);
        void UpdateRequestPrompts(string requestId, string systemPrompt, string userPrompt, string assistantPrompt);
        void CompleteRequest(string requestId, string response, int tokensUsed, int elapsedMs);
        void FailRequest(string requestId, string error);
        void FailRequest(string requestId, string error, int elapsedMs);
        void AddToolCall(string requestId, string toolCallId, string toolName, bool succeeded, string? error);
        void Clear();
    }
}
