using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Json;
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
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

        private static readonly Regex ThinkingTagBlockRegex = new Regex(
            @"<thinking>[\s\S]*?</thinking>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex ResidualXmlTagRegex = new Regex(
            @"</?(?:tool_call|speech|thought|dialogue|response|monologue|reply|thinking)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

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
                if (context.Items.TryGetValue(PipelineContextKeys.FallbackContent, out var fb) && fb is string fallbackStr && !string.IsNullOrWhiteSpace(fallbackStr))
                {
                    context.Result = Result<LlmResponse, RimMindError>.Ok(response.With(content: fallbackStr));
                    return;
                }

                context.ShortCircuit("output_guardrail:empty_response");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("Output guardrail: LLM returned empty response with no tool calls"));
                return;
            }

            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                var declaredTools = context.Envelope?.Tools;
                if (string.IsNullOrWhiteSpace(response.ToolCallsJson)
                    && declaredTools != null
                    && declaredTools.Count > 0)
                {
                    var parsedCalls = LlmPayloadParser.ParsePseudoFunctionCalls(response.Content);
                    var firstCall = parsedCalls.Count > 0 ? parsedCalls[0] : null;
                    if (firstCall != null
                        && !string.IsNullOrWhiteSpace(firstCall.Name)
                        && !string.IsNullOrWhiteSpace(firstCall.ArgumentsJson)
                        && declaredTools.Any(t => string.Equals(t.Name, firstCall.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        string synthesizedToolCallsJson = JsonConvert.SerializeObject(new[]
                        {
                            new
                            {
                                id = string.IsNullOrWhiteSpace(firstCall.Id) ? "call_pseudo_0" : firstCall.Id,
                                type = "function",
                                function = new
                                {
                                    name = firstCall.Name,
                                    arguments = firstCall.ArgumentsJson
                                }
                            }
                        });
                        response = response.With(toolCallsJson: synthesizedToolCallsJson);
                        context.Result = Result<LlmResponse, RimMindError>.Ok(response);
                    }
                }

                var cleanedContent = StripResidualModelXmlTags(response.Content!);
                if (!string.IsNullOrWhiteSpace(cleanedContent) && !string.Equals(cleanedContent, response.Content, StringComparison.Ordinal))
                {
                    response = response.With(content: cleanedContent);
                    context.Result = Result<LlmResponse, RimMindError>.Ok(response);
                }
            }

            if (IsRepetitiveAction(context, response))
            {
                context.ShortCircuit("output_guardrail:repetitive_action");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.PipelineShortCircuited("Output guardrail: Repetitive action detected, short-circuiting"));
                return;
            }
        }

        internal static string StripResidualModelXmlTags(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            string result = content;
            try
            {
                if (result.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = ThinkingTagBlockRegex.Replace(result, string.Empty);
                }

                if (result.IndexOf('<') >= 0 && result.IndexOf('>') >= 0)
                {
                    result = ResidualXmlTagRegex.Replace(result, string.Empty);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            result = result.Trim();

            if (result.IndexOf('(') > 0)
            {
                var parsed = LlmPayloadParser.ParsePseudoFunctionCalls(result);
                string? argsJson = parsed.Count > 0 ? parsed[0].ArgumentsJson : null;
                if (!string.IsNullOrWhiteSpace(argsJson))
                {
                    try
                    {
                        var obj = JObject.Parse(argsJson!);
                        string? innerText = obj["speech"]?.ToString()
                                         ?? obj["thought"]?.ToString()
                                         ?? obj["reply"]?.ToString()
                                         ?? obj["dialogue"]?.ToString()
                                         ?? obj["content"]?.ToString()
                                         ?? obj["text"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(innerText))
                        {
                            result = innerText!.Trim();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return result.Trim();
        }

        private static bool IsRepetitiveAction(LlmRequestContext context, LlmResponse response)
        {
            bool hasToolCalls = !string.IsNullOrWhiteSpace(response.ToolCallsJson)
                || (context.ToolCallResults != null && context.ToolCallResults.Count > 0);
            if (!hasToolCalls) return false;
            if (!context.Items.TryGetValue(PipelineContextKeys.RecentActionIntents, out var obj)) return false;
            if (obj is not List<string> recentIntents) return false;
            if (recentIntents.Count < MaxRepetitiveCount) return false;

            var lastN = recentIntents.Skip(Math.Max(0, recentIntents.Count - MaxRepetitiveCount)).ToList();
            return lastN.Distinct().Count() == 1;
        }
    }
}
