using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class ToolCallDispatchMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedToolCallDispatch";
        public int Order => RimMindDefaults.MiddlewareOrder.ToolCallDispatch;
        public string Id => "UnifiedToolCallDispatch";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly IToolRegistry _toolRegistry;
        private readonly ILogSink? _log;
        private readonly IAIRequestTraceLog? _traceLog;

        public ToolCallDispatchMiddleware(
            IToolRegistry toolRegistry,
            ILogSink? log = null,
            IAIRequestTraceLog? traceLog = null)
        {
            _toolRegistry = toolRegistry;
            _log = log;
            _traceLog = traceLog;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            await next(context);

            // After the pipeline completes, check if the response contains tool calls
            if (context.Result?.IsOk != true) return;
            if (context.IsShortCircuited) return;
            if (context.Envelope?.ToolDispatchMode == ToolCallDispatchMode.Manual)
            {
                _log?.Message($"[UnifiedToolCallDispatch] Manual dispatch requested for request {context.Envelope?.RequestId}; leaving tool_calls for owner.");
                return;
            }

            var response = context.Result.Value.Value;
            if (response == null || response.ToolCallsJson == null) return;

            var toolCalls = ParseToolCalls(response.ToolCallsJson);
            if (toolCalls == null || toolCalls.Count == 0) return;

            _log?.Message($"[UnifiedToolCallDispatch] Dispatching {toolCalls.Count} tool call(s) for request {context.Envelope?.RequestId}");

            var results = new List<ToolResult>();
            var requestId = context.Envelope?.RequestId ?? string.Empty;
            foreach (var tc in toolCalls)
            {
                var result = await DispatchToolCallAsync(tc, context, context.Ct);
                results.Add(result);
                _traceLog?.AddToolCall(
                    requestId,
                    result.ToolCallId ?? tc.Id,
                    result.ToolName ?? tc.FunctionName,
                    !result.IsError,
                    result.IsError ? result.Content : null);
            }

            // Store results in context for downstream consumers
            context.ToolCallResults = results;

            _log?.Message($"[UnifiedToolCallDispatch] Completed {results.Count} tool call(s): " +
                $"{results.Count(r => !r.IsError)} ok, {results.Count(r => r.IsError)} errors");
        }

        private async Task<ToolResult> DispatchToolCallAsync(ToolCallEntry entry, LlmRequestContext context, CancellationToken ct)
        {
            var handler = _toolRegistry.FindById(entry.FunctionName);
            if (handler == null)
            {
                _log?.Warning($"[UnifiedToolCallDispatch] No handler found for tool: {entry.FunctionName}");
                return ToolResult.Fail($"Unknown tool: {entry.FunctionName}", entry.Id, entry.FunctionName);
            }

            var args = new ToolCallArgs
            {
                ToolCallId = entry.Id,
                ToolName = entry.FunctionName,
                ArgumentsJson = entry.ArgumentsJson,
                NpcId = context.Envelope?.NpcId,
                Ct = ct,
                TraceId = context.TraceId
            };

            var result = await handler.ExecuteAsync(args, ct);
            return result.Match(
                ok =>
                {
                    _log?.Message($"[RimMind.ToolCall] action=Dispatched toolName={entry.FunctionName} toolCallId={entry.Id} npcId={context.Envelope?.NpcId ?? "none"}");
                    return ok with { ToolName = entry.FunctionName };
                },
                err =>
                {
                    _log?.Warning($"[RimMind.ToolCall] action=Failed toolName={entry.FunctionName} toolCallId={entry.Id} npcId={context.Envelope?.NpcId ?? "none"} error={err.Message}");
                    return ToolResult.Fail(err.Message, entry.Id, entry.FunctionName);
                });
        }

        private List<ToolCallEntry>? ParseToolCalls(string json)
        {
            try
            {
                var dtos = JsonConvert.DeserializeObject<List<ToolCallDtoInternal>>(json);
                if (dtos == null) return null;
                return dtos.Select(d => new ToolCallEntry
                {
                    Id = d.id ?? "",
                    Type = d.type ?? "function",
                    FunctionName = d.function?.name ?? "",
                    ArgumentsJson = d.function?.arguments ?? "{}"
                }).ToList();
            }
            catch (JsonException ex)
            {
                _log?.Warning($"[UnifiedToolCallDispatch] Failed to parse tool calls JSON: {ex.Message}");
                return null;
            }
        }

#pragma warning disable CS0649
        private class ToolCallDtoInternal
        {
            [JsonProperty("id")]
            public string? id;
            [JsonProperty("type")]
            public string? type;
            [JsonProperty("function")]
            public ToolCallFunctionDtoInternal? function;
        }

        private class ToolCallFunctionDtoInternal
        {
            [JsonProperty("name")]
            public string? name;
            [JsonProperty("arguments")]
            public string? arguments;
        }
#pragma warning restore CS0649
    }

    internal sealed class ToolCallEntry
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "function";
        public string FunctionName { get; init; } = "";
        public string ArgumentsJson { get; init; } = "{}";
    }
}
