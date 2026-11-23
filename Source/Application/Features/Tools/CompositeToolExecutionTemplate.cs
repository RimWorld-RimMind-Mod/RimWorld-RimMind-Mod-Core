using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Engine-free argument parsing, structured error construction, and guarded execution template
    /// shared by composite ToolCall implementations (e.g. Presentation's <c>CompositeToolCallBase</c>).
    /// Eliminates duplicated JSON parsing, preflight error handling, and exception-to-error boilerplate
    /// across composite action tools.
    /// </summary>
    public static class CompositeToolExecutionTemplate
    {
        public static JObject? TryParseArgumentsObject(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            try
            {
                return JToken.Parse(argumentsJson!) as JObject;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetArgument<T>(JObject? root, string key, out T value)
        {
            value = default!;
            if (root == null || string.IsNullOrEmpty(key))
                return false;

            try
            {
                var token = root[key];
                if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                    return false;

                if (typeof(T) == typeof(int) && token.Type == JTokenType.String)
                {
                    if (PawnIdParser.TryParse(token.ToString(), out int parsedInt))
                    {
                        value = (T)(object)parsedInt;
                        return true;
                    }
                    return false;
                }

                var converted = token.Value<T>();
                if (converted == null)
                    return false;

                value = converted;
                return true;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidCastException or ArgumentException)
            {
                return false;
            }
        }

        public static Result<T, RimMindError> TryGetArgumentOrError<T>(JObject? root, string key, string? traceId)
        {
            if (TryGetArgument(root, key, out T value))
                return Result<T, RimMindError>.Ok(value);

            return Result<T, RimMindError>.Err(InvalidActionError($"Missing or invalid {key}", traceId));
        }

        public static Result<int, RimMindError> TryGetPawnIdOrError(
            JObject? root,
            ToolCallArgs args,
            string key = "pawn_id")
        {
            if (args == null)
                return Result<int, RimMindError>.Err(InvalidActionError($"Missing or invalid {key}", null));

            int contextFallbackId = (args.PawnId.HasValue && args.PawnId.Value > 0)
                ? args.PawnId.Value
                : (PawnIdParser.TryParse(args.NpcId, out int parsedNpcId) && parsedNpcId > 0 ? parsedNpcId : 0);

            if (TryGetArgument<int>(root, key, out int id) && id > 0)
                return Result<int, RimMindError>.Ok(id);

            if (key != "pawn_id" && TryGetArgument<int>(root, "pawn_id", out int fallbackId) && fallbackId > 0)
                return Result<int, RimMindError>.Ok(fallbackId);

            if (contextFallbackId > 0)
                return Result<int, RimMindError>.Ok(contextFallbackId);

            return Result<int, RimMindError>.Err(InvalidActionError($"Missing or invalid {key}", args.TraceId));
        }

        public static RimMindError InvalidActionError(string message, string? traceId = null)
            => new RimMindError(RimMindErrorCode.MechanismInvalidAction, message) { TraceId = traceId };

        public static RimMindError PolicyDeniedError(string message, string? traceId = null)
            => new RimMindError(RimMindErrorCode.ToolPolicyDenied, message) { TraceId = traceId };

        public static RimMindError ExecutionFailedError(string message, string? traceId = null, Exception? inner = null)
            => new RimMindError(RimMindErrorCode.ToolExecutionFailed, message)
            {
                TraceId = traceId,
                InnerException = inner
            };

        /// <summary>
        /// Runs the standard composite-tool execution sequence:
        /// 1. Validates non-null args &amp; cancellation;
        /// 2. Parses JSON arguments once into a <see cref="JObject"/>;
        /// 3. Executes optional preflight guard (e.g., main-thread / colonist liveness / autonomy check);
        /// 4. Executes the tool body and converts unexpected exceptions into structured <see cref="RimMindError"/>.
        /// </summary>
        public static async Task<Result<ToolResult, RimMindError>> ExecuteWithGuardsAsync(
            string toolId,
            ToolCallArgs? args,
            Func<JObject?, ToolCallArgs, Result<Unit, RimMindError>>? preflightGuard,
            Func<JObject?, ToolCallArgs, CancellationToken, Task<Result<ToolResult, RimMindError>>> body,
            CancellationToken ct)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            if (ct.IsCancellationRequested)
                return Result<ToolResult, RimMindError>.Err(RimMindErrors.Cancelled());

            var effectiveArgs = args ?? new ToolCallArgs
            {
                ToolName = toolId,
                ArgumentsJson = "{}"
            };

            var root = TryParseArgumentsObject(effectiveArgs.ArgumentsJson);

            if (preflightGuard != null)
            {
                var guardResult = preflightGuard(root, effectiveArgs);
                if (guardResult.IsErr)
                    return Result<ToolResult, RimMindError>.Err(guardResult.Error);
            }

            try
            {
                return await body(root, effectiveArgs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result<ToolResult, RimMindError>.Err(RimMindErrors.Cancelled());
            }
            catch (Exception ex)
            {
                return Result<ToolResult, RimMindError>.Err(
                    ExecutionFailedError($"Tool '{toolId}' failed: {ex.Message}", effectiveArgs.TraceId, ex));
            }
        }
    }
}
