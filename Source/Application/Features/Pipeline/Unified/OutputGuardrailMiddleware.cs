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
                if (context.Items.TryGetValue("fallback_content", out var fb) && fb is string fallbackStr && !string.IsNullOrWhiteSpace(fallbackStr))
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
                    var (_, extractedToolName, extractedArgsJson, _) =
                        RimMind.Application.Features.Agent.Modes.ThinkStrategyHelper.TryExtractToolCallFromContent(response.Content);
                    if (!string.IsNullOrWhiteSpace(extractedToolName)
                        && !string.IsNullOrWhiteSpace(extractedArgsJson)
                        && declaredTools.Any(t => string.Equals(t.Name, extractedToolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        string synthesizedToolCallsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new[]
                        {
                            new
                            {
                                id = "call_pseudo_0",
                                type = "function",
                                function = new
                                {
                                    name = extractedToolName,
                                    arguments = extractedArgsJson
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

            if (IsRepetitiveAction(context))
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
            if (result.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result = System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"<thinking>[\s\S]*?</thinking>",
                    string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            if (result.IndexOf('<') >= 0 && result.IndexOf('>') >= 0)
            {
                result = System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"</?(?:tool_call|speech|thought|dialogue|response|monologue|reply|thinking)\b[^>]*>",
                    string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            result = result.Trim();

            if (result.IndexOf('(') > 0)
            {
                var (_, _, argsJson, _) =
                    RimMind.Application.Features.Agent.Modes.ThinkStrategyHelper.TryExtractToolCallFromContent(result);
                if (!string.IsNullOrWhiteSpace(argsJson))
                {
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(argsJson!);
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
