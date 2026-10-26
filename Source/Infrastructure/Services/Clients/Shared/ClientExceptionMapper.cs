using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Maps client exceptions to <see cref="RimMindError"/> results with consistent logging.
    /// Eliminates duplicate 3-catch patterns across OpenAI and Player2 clients.
    /// </summary>
    internal static class ClientExceptionMapper
    {
        /// <summary>
        /// Maps an exception to a Result error using the standard client error hierarchy:
        /// <list type="bullet">
        /// <item><see cref="OperationCanceledException"/> while <paramref name="callerToken"/> is cancelled → <see cref="RimMindErrors.Cancelled"/></item>
        /// <item><see cref="OperationCanceledException"/> / <see cref="TimeoutException"/> otherwise (our own per-request timeout fired) → <see cref="RimMindErrors.Timeout"/>,
        /// which the circuit breaker counts and <c>HybridAIClient</c> treats as retryable</item>
        /// <item><see cref="HttpTransport.HttpException"/> → <see cref="RimMindErrors.ClientTransient"/> or <see cref="RimMindErrors.ClientPermanent"/> based on StatusCode</item>
        /// <item>Network failures (<see cref="HttpRequestException"/>, <see cref="IOException"/>) → <see cref="RimMindErrors.ClientTransient"/></item>
        /// <item>Other exceptions → <see cref="RimMindErrors.Internal"/> (or ClientTransient if <paramref name="useClientTransientForGeneric"/> is true)</item>
        /// </list>
        /// Logged and returned messages are redacted and truncated (<see cref="LogSanitizer"/>).
        /// </summary>
        public static Result<LlmResponse, RimMindError> MapException(
            Exception ex,
            string clientName,
            string requestId,
            string operationLabel,
            ILogSink? logSink,
            bool useClientTransientForGeneric = false,
            CancellationToken callerToken = default)
        {
            string logPrefix = $"{clientName} {operationLabel}";

            if (ex is OperationCanceledException)
            {
                if (callerToken.IsCancellationRequested)
                {
                    logSink?.LogFromBackground($"[RimMind-Core] {logPrefix} cancelled ({requestId})", isWarning: false);
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
                }

                logSink?.LogFromBackground($"[RimMind-Core] {logPrefix} timed out ({requestId})", isWarning: true);
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Timeout($"{clientName} {operationLabel} timed out ({requestId})"));
            }

            if (ex is TimeoutException)
            {
                logSink?.LogFromBackground($"[RimMind-Core] {logPrefix} timed out ({requestId}): {LogSanitizer.ForLog(ex.Message)}", isWarning: true);
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Timeout($"{clientName} {operationLabel} timed out ({requestId})"));
            }

            if (ex is HttpTransport.HttpException httpEx)
            {
                logSink?.LogFromBackground($"[RimMind-Core] {logPrefix} failed ({requestId}): {httpEx.Message}", isWarning: true);
                return Result<LlmResponse, RimMindError>.Err(
                    IsTransientHttpFailure(httpEx)
                        ? RimMindErrors.ClientTransient(httpEx.Message, httpEx)
                        : RimMindErrors.ClientPermanent(httpEx.Message, httpEx));
            }

            string safeMessage = LogSanitizer.ForLog(ex.Message);
            logSink?.LogFromBackground($"[RimMind-Core] {logPrefix} failed ({requestId}): {safeMessage}", isWarning: true);

            if (ex is HttpRequestException || ex is IOException || ex is System.Net.WebException || ex is System.Net.Sockets.SocketException)
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient(safeMessage, ex));

            if (useClientTransientForGeneric)
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient(safeMessage, ex));
            return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Internal($"{clientName} {operationLabel} failed: {safeMessage}", ex));
        }

        /// <summary>
        /// Status/keyword classification shared by buffered and streaming paths:
        /// 401/402/403 and quota/auth keywords are permanent; 408/429/5xx and rate-limit/queue keywords are transient.
        /// </summary>
        internal static bool IsTransientHttpFailure(HttpTransport.HttpException httpEx)
        {
            string msg = httpEx.ClassificationText;
            bool isRateLimitOrQueue =
                msg.IndexOf("rate_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("concurrency_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("queue_full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Too many pending requests", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isPermanentQuotaOrAuth =
                httpEx.StatusCode == 401 ||
                httpEx.StatusCode == 402 ||
                httpEx.StatusCode == 403 ||
                msg.IndexOf("Insufficient account funds", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0;
            return !isPermanentQuotaOrAuth &&
                (httpEx.StatusCode == 408 || httpEx.StatusCode == 429 || httpEx.StatusCode >= 500 || isRateLimitOrQueue);
        }
    }
}
