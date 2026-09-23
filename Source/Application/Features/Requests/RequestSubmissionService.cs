using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Requests
{
    public sealed class RequestSubmissionService : IRequestSubmissionService
    {
        private readonly IRequestQueue _queue;
        private readonly IClientManager _clientManager;
        private readonly IPipeline<LlmRequestContext> _pipeline;
        private readonly IAIRequestTraceLog? _traceLog;
        private readonly IAIModelSettings? _modelSettings;
        private readonly ICompletionFence _completionFence;

        public RequestSubmissionService(
            IRequestQueue queue,
            IClientManager clientManager,
            IPipeline<LlmRequestContext> pipeline,
            IAIRequestTraceLog? traceLog,
            IAIModelSettings? modelSettings,
            ICompletionFence completionFence)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _traceLog = traceLog;
            _modelSettings = modelSettings;
            _completionFence = completionFence ?? throw new ArgumentNullException(nameof(completionFence));
        }

        public void Send(
            LlmRequestEnvelope envelope,
            Action<Result<LlmResponse, RimMindError>> onComplete)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            Send(envelope, (result, _) => onComplete(result));
        }

        public void Send(
            LlmRequestEnvelope envelope,
            Action<Result<LlmResponse, RimMindError>, LlmRequestContext?> onComplete)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));

            var client = _clientManager.GetClient();
            if (client == null)
            {
                onComplete(
                    Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientNotConfigured("No AI client available")),
                    null);
                return;
            }

            var elapsed = Stopwatch.StartNew();
            StartTrace(envelope);
            var executor = new QueuedPipelineRequestExecutor(_pipeline, client, envelope);
            _queue.Enqueue(
                envelope,
                result =>
                {
                    elapsed.Stop();
                    CompleteTrace(envelope.RequestId, result, elapsed.ElapsedMilliseconds);
                    onComplete(result, executor.Context);
                },
                cancellationToken => executor.ExecuteAsync(envelope, cancellationToken),
                client.IsLocalEndpoint);
        }

        public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            var completion = new TaskCompletionSource<Result<LlmResponse, RimMindError>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void CompleteCancelled() => completion.TrySetResult(
                Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled()));

            if (!RequestCancellationRegistrations.TryCreate(
                    _completionFence.CancellationToken,
                    envelope.Ct,
                    CompleteCancelled,
                    out var registrations,
                    out var setupFailure))
            {
                var error = setupFailure is ObjectDisposedException
                    ? RimMindErrors.Cancelled()
                    : RimMindErrors.Internal(
                        "Failed to register request cancellation.",
                        setupFailure);
                return Task.FromResult(Result<LlmResponse, RimMindError>.Err(error));
            }

            var ownedRegistrations = registrations ?? throw new InvalidOperationException(
                "Cancellation registration succeeded without an owner.");

            try
            {
                if (!completion.Task.IsCompleted)
                    Send(envelope, result => completion.TrySetResult(result));
            }
            catch
            {
                ownedRegistrations.Dispose();
                throw;
            }

            _ = completion.Task.ContinueWith(
                _ => ownedRegistrations.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return completion.Task;
        }

        private void StartTrace(LlmRequestEnvelope envelope)
        {
            _traceLog?.StartRequest(
                envelope.RequestId,
                GetTraceSource(envelope),
                _modelSettings?.ModelName ?? string.Empty,
                BuildTracePrompt(envelope, "system"),
                BuildTracePrompt(envelope, "user"),
                BuildTracePrompt(envelope, "assistant"));
        }

        private void CompleteTrace(
            string requestId,
            Result<LlmResponse, RimMindError> result,
            long elapsedMilliseconds)
        {
            if (result.IsOk)
            {
                var response = result.Value;
                string responseContent = !string.IsNullOrWhiteSpace(response.Content)
                    ? response.Content
                    : response.ToolCallsJson ?? string.Empty;

                _traceLog?.CompleteRequest(
                    requestId,
                    responseContent,
                    response.TokensUsed,
                    (int)elapsedMilliseconds);

                if (!string.IsNullOrWhiteSpace(response.ToolCallsJson) && _traceLog != null)
                {
                    try
                    {
                        var calls = Newtonsoft.Json.JsonConvert.DeserializeObject<List<StructuredToolCall>>(response.ToolCallsJson!);
                        if (calls != null)
                        {
                            foreach (var call in calls)
                            {
                                if (!string.IsNullOrWhiteSpace(call.Name))
                                {
                                    _traceLog.AddToolCall(requestId, call.Id, call.Name, succeeded: true, error: null);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore trace parse failure
                    }
                }
                return;
            }

            _traceLog?.FailRequest(
                requestId,
                result.Error.Message,
                (int)elapsedMilliseconds);
        }

        private static string GetTraceSource(LlmRequestEnvelope envelope)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(envelope.ModId)) parts.Add($"mod:{envelope.ModId}");
            if (!string.IsNullOrWhiteSpace(envelope.ScenarioId)) parts.Add($"scenario:{envelope.ScenarioId}");
            if (!string.IsNullOrWhiteSpace(envelope.NpcId)) parts.Add($"npc:{envelope.NpcId}");
            return parts.Count > 0 ? string.Join(" | ", parts) : "unknown";
        }

        private static string BuildTracePrompt(LlmRequestEnvelope envelope, string role)
        {
            if (envelope.Messages == null || envelope.Messages.Count == 0)
                return string.Empty;

            var prompt = new StringBuilder();
            foreach (var message in envelope.Messages)
            {
                if (!string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(message.Content))
                    continue;

                if (prompt.Length > 0)
                    prompt.AppendLine().AppendLine();
                if (!string.IsNullOrWhiteSpace(message.LayerTag))
                    prompt.Append('[').Append(message.LayerTag).Append("] ");
                prompt.Append(message.Content);
            }

            return prompt.ToString();
        }
    }
}
