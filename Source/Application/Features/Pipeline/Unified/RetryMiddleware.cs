using System;
using System.Threading;
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
    internal sealed class RetryMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedRetry";
        public int Order => RimMindDefaults.MiddlewareOrder.UnifiedRetry;
        public string Id => "UnifiedRetry";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly int _maxRetries;
        private readonly TimeSpan _delay;
        private readonly ILogSink? _log;

        public RetryMiddleware(int maxRetries = RimMindDefaults.DefaultMaxRetryCount, TimeSpan? delay = null, ILogSink? log = null)
        {
            _maxRetries = maxRetries;
            _delay = delay ?? TimeSpan.FromSeconds(2);
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            int maxAttempts = context.Envelope?.MaxRetryCount ?? _maxRetries;
            for (int attempt = 0; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await next(context);
                    if (context.Result?.IsOk == true)
                        return;
                    if (context.Result.HasValue && context.Result.Value.IsErr && context.Result.Value.Error.Code == RimMindErrorCode.ClientPermanentFailure)
                        return;
                    if (context.IsShortCircuited && context.ShortCircuitReason != "transient_error")
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log?.Warning($"[UnifiedRetry] Attempt {attempt + 1}/{maxAttempts + 1} failed: {ex.Message}");
                }

                if (attempt < maxAttempts)
                {
                    context.RetryCount = attempt + 1;
                    context.Result = null;
                    _log?.Message($"[UnifiedRetry] Retrying request {context.Envelope?.RequestId}, attempt {attempt + 2}");
                    await Task.Delay(_delay, context.Ct);
                }
            }
        }
    }
}
