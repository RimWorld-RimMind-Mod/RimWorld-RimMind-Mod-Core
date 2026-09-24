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
    internal sealed class InputGuardrailMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "input_guardrail";
        public int Order => RimMindDefaults.MiddlewareOrder.InputGuardrail;
        public string Id => "core.input_guardrail";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly IInputGuardrail[] _guardrails;
        private readonly ILogSink? _log;

        public InputGuardrailMiddleware(IInputGuardrail[] guardrails, ILogSink? log = null)
        {
            _guardrails = guardrails;
            _log = log;
        }

        public Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            foreach (var guardrail in _guardrails)
            {
                var result = guardrail.Check(context.Envelope);
                if (!result.Passed)
                {
                    _log?.Warning($"[InputGuardrail] {guardrail.Name} rejected: {result.Reason}");
                    context.Result = Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.PipelineShortCircuited($"Input guardrail '{guardrail.Name}': {result.Reason}"));
                    context.ShortCircuit($"input_guardrail:{guardrail.Name}");
                    return Task.CompletedTask;
                }
            }

            return next(context);
        }
    }
}
