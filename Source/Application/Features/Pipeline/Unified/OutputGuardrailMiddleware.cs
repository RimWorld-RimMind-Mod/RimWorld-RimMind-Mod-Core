using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.ValueObjects;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class OutputGuardrailMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "output_guardrail";
        public int Order => RimMindDefaults.MiddlewareOrder.OutputGuardrail;
        public string Id => "core.output_guardrail";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private const int MaxRepetitiveCount = 3;

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            await next(context);

            if (context.Result?.IsOk != true) return;
            if (context.IsShortCircuited) return;

            var response = context.Result.Value.Value;
            if (response == null) return;

            if (string.IsNullOrWhiteSpace(response.Content)
                && string.IsNullOrEmpty(response.ToolCallsJson))
            {
                context.ShortCircuit("output_guardrail:empty_response");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("Output guardrail: LLM returned empty response with no tool calls"));
                return;
            }

            if (IsRepetitiveAction(context))
            {
                context.ShortCircuit("output_guardrail:repetitive_action");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("Output guardrail: Repetitive action detected, short-circuiting"));
                return;
            }
        }

        private bool IsRepetitiveAction(LlmRequestContext context)
        {
            if (context.ToolCallResults == null || context.ToolCallResults.Count == 0) return false;
            if (!context.Items.TryGetValue("recent_action_intents", out var obj)) return false;
            if (obj is not List<string> recentIntents) return false;
            if (recentIntents.Count < MaxRepetitiveCount) return false;

            var lastN = recentIntents.Skip(Math.Max(0, recentIntents.Count - MaxRepetitiveCount)).ToList();
            return lastN.Distinct().Count() == 1;
        }
    }
}
