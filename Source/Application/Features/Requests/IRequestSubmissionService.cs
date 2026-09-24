using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Requests
{
    public interface IRequestSubmissionService
    {
        void Send(
            LlmRequestEnvelope envelope,
            Action<Result<LlmResponse, RimMindError>> onComplete);

        void Send(
            LlmRequestEnvelope envelope,
            Action<Result<LlmResponse, RimMindError>, LlmRequestContext?> onComplete);

        Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope);
    }
}
