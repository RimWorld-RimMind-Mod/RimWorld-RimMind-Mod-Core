using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class TelemetryMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedTelemetry";
        public int Order => RimMindDefaults.MiddlewareOrder.Telemetry;
        public string Id => "UnifiedTelemetry";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ITelemetryCollector? _telemetry;
        private readonly ILogSink? _log;

        public TelemetryMiddleware(ITelemetryCollector? telemetry = null, ILogSink? log = null)
        {
            _telemetry = telemetry;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            var start = DateTime.UtcNow;

            await next(context);

            var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;

            var tags = new System.Collections.Generic.Dictionary<string, string>
            {
                { "scenario", context.Envelope?.ScenarioId ?? "unknown" },
                { "is_streaming", (context.Envelope?.IsStreaming ?? false).ToString() },
                { "npc_id", context.Envelope?.NpcId ?? "none" },
                { "mode_id", context.AgentModeId.Value },
                { "trace_id", context.TraceId ?? "none" },
                { "tool_call_count", (context.ToolCallResults?.Count ?? 0).ToString() }
            };

            // Record timing
            _telemetry?.Record("unified_request_duration_ms", (float)elapsed, tags);

            // Record success/failure
            if (context.Result != null && context.Result.Value.IsOk)
            {
                _telemetry?.Record("unified_request_success", 1, tags);
                var response = context.Result.Value.Value;
                if (response != null)
                {
                    _telemetry?.Record("unified_tokens_used", response.TokensUsed, tags);
                    _telemetry?.Record("unified_prompt_tokens", response.PromptTokens, tags);
                    _telemetry?.Record("unified_completion_tokens", response.CompletionTokens, tags);
                }
            }
            else if (context.Result != null && context.Result.Value.IsErr)
            {
                tags["error_code"] = context.Result.Value.Error.Code.ToString();
                _telemetry?.Record("unified_request_failure", 1, tags);
                _log?.Warning($"[UnifiedTelemetry] Request failed: {context.Result.Value.Error.Message} ({elapsed:F0}ms)");
            }
            else if (context.IsShortCircuited)
            {
                tags["reason"] = context.ShortCircuitReason ?? "unknown";
                _telemetry?.Record("unified_request_short_circuit", 1, tags);
            }

            _log?.Message($"[UnifiedTelemetry] Request {context.Envelope?.RequestId} completed in {elapsed:F0}ms");
        }
    }
}
