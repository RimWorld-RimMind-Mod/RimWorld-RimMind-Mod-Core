using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Requests
{
    /// <summary>
    /// Adapts a unified-pipeline invocation to the queue's cancellable executor contract.
    /// </summary>
    public sealed class QueuedPipelineRequestExecutor
    {
        private readonly IPipeline<LlmRequestContext> _pipeline;
        private readonly IAIClient _client;

        public LlmRequestContext? Context { get; private set; }

        public QueuedPipelineRequestExecutor(
            IPipeline<LlmRequestContext> pipeline,
            IAIClient client,
            LlmRequestEnvelope envelope)
        {
            _pipeline = pipeline;
            _client = client;
            Context = new LlmRequestContext(envelope, ct: envelope.Ct) { Client = client };
        }

        public async Task<Result<LlmResponse, RimMindError>> ExecuteAsync(
            LlmRequestEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Context = new LlmRequestContext(envelope, ct: cancellationToken) { Client = _client };
            await _pipeline.ExecuteAsync(Context).ConfigureAwait(false);
            return Context.Result ?? Result<LlmResponse, RimMindError>.Err(
                RimMindErrors.Internal("Pipeline produced no result."));
        }
    }
}
