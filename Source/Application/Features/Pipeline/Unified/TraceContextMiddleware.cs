using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class TraceContextMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedTraceContext";
        public int Order => RimMindDefaults.MiddlewareOrder.TraceContext;
        public string Id => "UnifiedTraceContext";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;

        public TraceContextMiddleware(ILogSink? log = null)
        {
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            var traceId = context.Envelope?.TraceId ?? context.TraceId;
            using (TraceContext.BeginScope(traceId))
            {
                _log?.Message($"[UnifiedTraceContext] Trace scope set: {traceId}");
                await next(context);
            }
        }
    }
}
