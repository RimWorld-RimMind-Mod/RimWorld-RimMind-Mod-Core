using System.Collections.Generic;
using RimMind.Application.Common.Behaviours;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Context;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public static class UnifiedRequestPipelineFactory
    {
        public static MutablePipeline<LlmRequestContext> Build(
            IToolRegistry toolRegistry,
            ILogSink? log = null,
            INpcManagerAccessor? npcManagers = null,
            IContextEngine? contextEngine = null,
            ITelemetryCollector? telemetry = null,
            ICircuitBreakerSettings? circuitBreakerSettings = null,
            IExtensionRegistry<IMiddleware<LlmRequestContext>>? extensions = null,
            IRelevanceLearner? relevanceLearner = null,
            AIResponseAnalyzer? responseAnalyzer = null,
            IAIRequestTraceLog? requestTraceLog = null)
        {
            var analyzer = responseAnalyzer ?? new AIResponseAnalyzer();
            var middlewares = new List<IMiddleware<LlmRequestContext>>
            {
                new ShortCircuitMiddleware(log),
                new TraceContextMiddleware(log),
                new NpcEnrichMiddleware(npcManagers, log),
                new InputGuardrailMiddleware(new IInputGuardrail[]
                {
                    new EmptyPerceptionGuardrail(),
                    new BudgetOverflowGuardrail(),
                    new RepetitionGuardrail()
                }, log),
                new ContextBuildMiddleware(contextEngine, log),
                new ContextFeedbackMiddleware(relevanceLearner ?? new RelevanceLearner(), analyzer, log),
                new RequestSanitizeMiddleware(log),
                new CacheMiddleware(log),
                new TelemetryMiddleware(telemetry, log),
                new CircuitBreakerMiddleware(circuitBreakerSettings, log),
                new RetryMiddleware(log: log),
                new ClientInvokeMiddleware(log, requestTraceLog),
                new ToolCallDispatchMiddleware(toolRegistry, log, requestTraceLog),
                new OutputGuardrailMiddleware()
            };

            var pipeline = new MutablePipeline<LlmRequestContext>();
            pipeline.UseRange(middlewares);
            if (extensions != null)
            {
                pipeline.SetExtensionRegistry(extensions);
            }
            return pipeline;
        }
    }
}
