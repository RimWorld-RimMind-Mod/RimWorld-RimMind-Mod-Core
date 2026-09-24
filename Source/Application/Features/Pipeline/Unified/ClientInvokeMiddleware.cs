using System;
using RimMind.Application.Features.Llm;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class ClientInvokeMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedClientInvoke";
        public int Order => RimMindDefaults.MiddlewareOrder.ClientInvoke;
        public string Id => "UnifiedClientInvoke";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;
        private readonly IAIRequestTraceLog? _traceLog;

        public ClientInvokeMiddleware(ILogSink? log = null, IAIRequestTraceLog? traceLog = null)
        {
            _log = log;
            _traceLog = traceLog;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            var client = context.Client;
            if (client == null)
            {
                _log?.Warning("[UnifiedClientInvoke] No client available");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientNotConfigured("UnifiedClientInvoke"));
                context.ShortCircuit("NoClient");
                return;
            }

            context.Envelope = CloneWithCancellationToken(context.Envelope, context.Ct);
            if (context.Envelope.IsStreaming && client.SupportsStreaming)
            {
                await InvokeStreamingAsync(context, client);
            }
            else
            {
                await InvokeNonStreamingAsync(context, client);
            }

            await next(context);
        }

        private async Task InvokeNonStreamingAsync(LlmRequestContext context, IAIClient client)
        {
            RecordFinalPrompts(context.Envelope);
            var result = await client.SendAsync(context.Envelope);
            if (result.IsOk)
            {
                context.Result = result;
            }
            else
            {
                _log?.Warning($"[UnifiedClientInvoke] Error: {result.Error.Message}");
                context.Result = result;
            }
        }

        private async Task InvokeStreamingAsync(LlmRequestContext context, IAIClient client)
        {
            RecordFinalPrompts(context.Envelope);
            // Streaming: use SendStreamAsync with callback; the Result now carries the final LlmResponse
            var aggregator = new ChunkAggregator(context.Envelope.RequestId);
            var result = await client.SendStreamAsync(context.Envelope, chunk =>
            {
                aggregator.Append(chunk);
            }, context.Ct);

            if (result.IsErr)
            {
                _log?.Warning($"[UnifiedClientInvoke] Stream error: {result.Error.Message}");
                aggregator.SetError(result.Error);
                var errorResult = aggregator.BuildFinalResponse();
                context.Result = errorResult;
                return;
            }

            // Use the LlmResponse from the Result (authoritative), enriched with pipeline metadata
            var response = result.Value;
            var processingMs = (long)(DateTime.UtcNow - context.StartTimeUtc).TotalMilliseconds;
            var enriched = new LlmResponse
            {
                RequestId = response.RequestId,
                Content = response.Content,
                ToolCallsJson = response.ToolCallsJson,
                ReasoningContent = response.ReasoningContent,
                TokensUsed = response.TokensUsed,
                PromptTokens = response.PromptTokens,
                CompletionTokens = response.CompletionTokens,
                CachedTokens = response.CachedTokens,
                State = response.State,
                Priority = context.Envelope.Priority,
                AttemptCount = context.RetryCount + 1,
                QueueWaitMs = response.QueueWaitMs,
                ProcessingMs = processingMs,
                HttpStatusCode = response.HttpStatusCode,
            };

            context.Result = Result<LlmResponse, RimMindError>.Ok(enriched);
        }

        private void RecordFinalPrompts(LlmRequestEnvelope envelope)
        {
            if (_traceLog == null)
                return;

            _traceLog.UpdateRequestPrompts(
                envelope.RequestId,
                BuildPrompt(envelope, "system"),
                BuildPrompt(envelope, "user"),
                BuildPrompt(envelope, "assistant"));
        }

        private static string BuildPrompt(LlmRequestEnvelope envelope, string role)
        {
            var messages = envelope.Messages;
            if (messages == null || messages.Count == 0)
                return string.Empty;

            var text = new System.Text.StringBuilder();
            foreach (var message in messages)
            {
                if (!string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                if (text.Length > 0)
                    text.AppendLine().AppendLine();
                if (!string.IsNullOrWhiteSpace(message.LayerTag))
                    text.Append('[').Append(message.LayerTag).Append("] ");
                text.Append(message.Content);
            }
            return text.ToString();
        }

        private static LlmRequestEnvelope CloneWithCancellationToken(
            LlmRequestEnvelope envelope,
            System.Threading.CancellationToken cancellationToken)
        {
            return new LlmRequestEnvelope
            {
                RequestId = envelope.RequestId,
                TraceId = envelope.TraceId,
                ScenarioId = envelope.ScenarioId,
                ModId = envelope.ModId,
                Messages = envelope.Messages,
                SystemAugmentations = envelope.SystemAugmentations,
                JsonSchema = envelope.JsonSchema,
                Tools = envelope.Tools,
                ToolDispatchMode = envelope.ToolDispatchMode,
                Examples = envelope.Examples,
                MaxTokens = envelope.MaxTokens,
                Temperature = envelope.Temperature,
                Priority = envelope.Priority,
                ExpireAtTicks = envelope.ExpireAtTicks,
                MaxRetryCount = envelope.MaxRetryCount,
                IsStreaming = envelope.IsStreaming,
                OnStreamChunk = envelope.OnStreamChunk,
                Ct = cancellationToken,
                NpcId = envelope.NpcId,
                GameStateInfo = envelope.GameStateInfo
            };
        }
    }
}
