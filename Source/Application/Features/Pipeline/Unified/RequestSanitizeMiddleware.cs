using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Prompt;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class RequestSanitizeMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedRequestSanitize";
        public int Order => RimMindDefaults.MiddlewareOrder.RequestSanitize;
        public string Id => "UnifiedRequestSanitize";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;

        public RequestSanitizeMiddleware(ILogSink? log = null)
        {
            _log = log;
        }

        public Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            if (context.Envelope == null)
            {
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Null envelope"));
                context.ShortCircuit("NullEnvelope");
                return Task.CompletedTask;
            }

            // Sanitize all message content
            if (context.Envelope.Messages != null)
            {
                foreach (var msg in context.Envelope.Messages)
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        msg.Content = string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase)
                            ? PromptSanitizer.SanitizeUserInput(msg.Content)
                            : PromptSanitizer.Sanitize(msg.Content);
                    }
                    if (msg.ReasoningContent is { Length: > 0 } reasoning)
                    {
                        msg.ReasoningContent = PromptSanitizer.Sanitize(reasoning);
                    }
                }
            }

            // Check for empty messages after sanitization
            if (context.Envelope.Messages == null || context.Envelope.Messages.Count == 0)
            {
                _log?.Warning("[UnifiedRequestSanitize] No messages after sanitization");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("EmptyMessages"));
                context.ShortCircuit("EmptyMessages");
                return Task.CompletedTask;
            }

            return next(context);
        }
    }
}
