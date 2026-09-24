using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Requests;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Request
        {
            private static readonly RuntimeServiceRef<IRequestQueue> Queues =
                RuntimeServiceRef<IRequestQueue>.Optional();
            private static readonly RuntimeServiceRef<IRequestSubmissionService> Submissions =
                RuntimeServiceRef<IRequestSubmissionService>.Optional();

            public static void PauseQueue() => Queues.ValueOrDefault?.PauseQueue();
            public static void ResumeQueue() => Queues.ValueOrDefault?.ResumeQueue();
            public static int ActiveRequestCount => Queues.ValueOrDefault?.ActiveRequestCount ?? 0;
            public static IReadOnlyList<TrackedRequest> GetActiveRequests() => Queues.ValueOrDefault?.GetActiveRequests() ?? new List<TrackedRequest>();
            public static IReadOnlyList<TrackedRequest> GetAllQueuedRequests() => Queues.ValueOrDefault?.GetAllQueuedRequests() ?? new List<TrackedRequest>();
            public static int TotalQueuedCount => Queues.ValueOrDefault?.TotalQueuedCount ?? 0;

            public static void ClearModCooldown(string modId) => Queues.ValueOrDefault?.ClearCooldown(modId);

            /// <summary>Remaining cooldown ticks for a mod (0 when ready or when queue is unavailable).</summary>
            public static int GetModCooldownTicksLeft(string modId)
                => Queues.ValueOrDefault?.GetCooldownTicksLeft(modId) ?? 0;

            /// <summary>Unified async request entry (callback style)</summary>
            public static void Send(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>> onComplete)
            {
                if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));

                var submission = Submissions.ValueOrDefault;
                if (submission == null)
                {
                    onComplete(Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.PipelineShortCircuited("runtime services unavailable")));
                    return;
                }

                submission.Send(envelope, onComplete);
            }

            /// <summary>Unified async request entry (callback with context style)</summary>
            public static void Send(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>, LlmRequestContext> onComplete)
            {
                if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));

                var submission = Submissions.ValueOrDefault;
                if (submission == null)
                {
                    onComplete(Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.PipelineShortCircuited("runtime services unavailable")), null!);
                    return;
                }

                submission.Send(envelope, (result, context) => onComplete(result, context!));
            }

            /// <summary>Unified async request entry (Task style)</summary>
            public static Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
            {
                var submission = Submissions.ValueOrDefault;
                return submission?.SendAsync(envelope) ?? Task.FromResult(
                    Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.PipelineShortCircuited("runtime services unavailable")));
            }
        }
    }
}
