using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class RetryMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedRetry";
        public int Order => RimMindDefaults.MiddlewareOrder.UnifiedRetry;
        public string Id => "UnifiedRetry";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly int _maxRetries;
        private readonly TimeSpan _delay;
        private readonly ILogSink? _log;
        private readonly IQueueSettings? _queueSettings;

        public RetryMiddleware(
            int maxRetries = RimMindDefaults.DefaultMaxRetryCount,
            TimeSpan? delay = null,
            ILogSink? log = null,
            IQueueSettings? queueSettings = null)
        {
            _maxRetries = maxRetries;
            _delay = delay ?? TimeSpan.FromSeconds(2);
            _log = log;
            _queueSettings = queueSettings;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            int configuredRetries = _queueSettings != null ? Math.Max(0, _queueSettings.MaxRetryCount) : _maxRetries;
            int maxAttempts = context.Envelope?.MaxRetryCount ?? configuredRetries;
            Result<LlmResponse, RimMindError>? lastResult = null;
            for (int attempt = 0; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await next(context).ConfigureAwait(false);
                    if (context.Result.HasValue)
                        lastResult = context.Result;
                    if (context.Result?.IsOk == true)
                        return;
                    if (context.Ct.IsCancellationRequested)
                        return;
                    if (context.Result.HasValue && context.Result.Value.IsErr &&
                        (context.Result.Value.Error.Code == RimMindErrorCode.ClientPermanentFailure ||
                         context.Result.Value.Error.Code == RimMindErrorCode.ContentFiltered ||
                         context.Result.Value.Error.Code == RimMindErrorCode.ClientQuotaOrAuthFailure ||
                         context.Result.Value.Error.Code == RimMindErrorCode.Cancelled))
                        return;
                    if (context.IsShortCircuited
                        && context.ShortCircuitReason != "transient_error"
                        && context.ShortCircuitReason != "output_guardrail:empty_response")
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastResult = Result<LlmResponse, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.ClientTransientFailure, ex.Message) { InnerException = ex });
                    _log?.Warning($"[UnifiedRetry] Attempt {attempt + 1}/{maxAttempts + 1} failed: {LogSecretRedactor.ForLog(ex.Message)}");
                }

                if (attempt < maxAttempts)
                {
                    context.RetryCount = attempt + 1;
                    context.Result = null;
                    context.ClearShortCircuit();
                    _log?.Message($"[UnifiedRetry] Retrying request {context.Envelope?.RequestId}, attempt {attempt + 2}");
                    await Task.Delay(_delay, context.Ct).ConfigureAwait(false);
                }
            }

            if (context.Result == null && lastResult.HasValue)
            {
                context.Result = lastResult;
            }
        }
    }
}
