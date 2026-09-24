using System;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Context;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class ContextFeedbackMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "ContextFeedback";
        public int Order => RimMindDefaults.MiddlewareOrder.ContextFeedback;
        public string Id => "ContextFeedback";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly IRelevanceLearner _learner;
        private readonly AIResponseAnalyzer _analyzer;
        private readonly ILogSink? _log;

        public ContextFeedbackMiddleware(IRelevanceLearner learner, AIResponseAnalyzer analyzer, ILogSink? log = null)
        {
            _learner = learner;
            _analyzer = analyzer;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext ctx, MiddlewareDelegate<LlmRequestContext> next)
        {
            // Before downstream: record which keys were included in the prompt
            if (ctx.Snapshot != null && ctx.Snapshot.IncludedKeys != null && ctx.Snapshot.IncludedKeys.Length > 0)
            {
                var keys = ctx.Snapshot.IncludedKeys.ToList();
                _learner.RecordInclusion(ctx.Envelope.TraceId, ctx.Envelope.ScenarioId, keys);
            }

            try
            {
                await next(ctx).ConfigureAwait(false);

                // After downstream: analyze response and record which keys were actually used
                if (ctx.Result != null && ctx.Result.Value.IsOk && ctx.Snapshot != null)
                {
                    var used = _analyzer.ExtractUsedKeys(ctx.Snapshot, ctx.Result.Value.Value);
                    _learner.RecordUsage(ctx.Envelope.TraceId, used);
                }
                else if (ctx.Result != null && ctx.Result.Value.IsErr)
                {
                    // Request failed: clean up pending trace with empty usage
                    _learner.RecordUsage(ctx.Envelope.TraceId, Array.Empty<string>());
                }
            }
            catch
            {
                // Exception: clean up pending trace with empty usage
                _learner.RecordUsage(ctx.Envelope.TraceId, Array.Empty<string>());
                throw;
            }
        }
    }
}
