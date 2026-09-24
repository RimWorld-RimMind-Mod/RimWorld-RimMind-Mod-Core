using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using DomainAIRequestPriority = RimMind.Domain.Llm.AIRequestPriority;
using DomainAIRequestState = RimMind.Domain.Llm.AIRequestState;

namespace RimMind.Application.Common.Models.Client
{
    public class TrackedRequest
    {
        public int TrackingId;
        public LlmRequestEnvelope Envelope = null!;
        public Action<Result<LlmResponse, RimMindError>> Callback = null!;
        public IAIClient Client = null!;
        public Func<CancellationToken, Task<Result<LlmResponse, RimMindError>>> Executor = null!;
        public CancellationTokenSource? CancellationSource;
        public int CompletionQueued;
        public bool IsLocalEndpointSnapshot;
        public DomainAIRequestState State;
        public int EnqueuedAtTick;
        public int StartedProcessingAtTick;
        public int AttemptCount;
        public int MaxAttempts;

        public string RequestId = string.Empty;
        public string ModId = string.Empty;
        public DomainAIRequestPriority Priority;
        public string Status = string.Empty;

        public TrackedRequest() { }

        public TrackedRequest(string requestId, string modId, DomainAIRequestPriority priority, string status)
        {
            RequestId = requestId;
            ModId = modId;
            Priority = priority;
            Status = status;
        }
    }
}
