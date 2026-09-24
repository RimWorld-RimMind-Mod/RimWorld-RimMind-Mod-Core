using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class CacheMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedCache";
        public int Order => RimMindDefaults.MiddlewareOrder.Cache;
        public string Id => "UnifiedCache";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;

        public CacheMiddleware(ILogSink? log = null)
        {
            _log = log;
        }

        public Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            // Streaming requests are not cacheable
            if (context.Envelope != null && context.Envelope.IsStreaming)
            {
                _log?.Message($"[UnifiedCache] Streaming request {context.Envelope.RequestId}, skipping cache");
                return next(context);
            }

            if (context.CacheHit)
            {
                _log?.Message($"[UnifiedCache] Cache hit for {context.Envelope?.RequestId}");
                context.ShortCircuit("CacheHit");
                return Task.CompletedTask;
            }

            return next(context);
        }
    }
}
