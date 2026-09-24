using System.Collections.Generic;
using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IAIDebugLog
    {
        IReadOnlyList<AIDebugEntry> Entries { get; }
        void Clear();
        void Record(LlmRequestEnvelope envelope, LlmResponse response, int elapsedMs);
    }

    public class AIDebugEntry
    {
        public int GameTick { get; set; }
        public string Source { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string FullSystemPrompt { get; set; } = string.Empty;
        public string FullUserPrompt { get; set; } = string.Empty;
        public string FullAssistantPrompt { get; set; } = string.Empty;
        public string FullResponse { get; set; } = string.Empty;
        public int ElapsedMs { get; set; }
        public int TokensUsed { get; set; }
        public bool IsError { get; set; }
        public string ErrorMsg { get; set; } = string.Empty;
        public AIRequestPriority Priority { get; set; }
        public AIRequestState State { get; set; }
        public int AttemptCount { get; set; }
        public long QueueWaitMs { get; set; }
        public long ProcessingMs { get; set; }
        public long HttpStatusCode { get; set; }
        public int RequestPayloadBytes { get; set; }
    }
}
