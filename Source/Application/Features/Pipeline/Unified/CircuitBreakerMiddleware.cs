using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class CircuitBreakerMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedCircuitBreaker";
        public int Order => RimMindDefaults.MiddlewareOrder.CircuitBreaker;
        public string Id => "UnifiedCircuitBreaker";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ICircuitBreakerSettings? _settings;
        private readonly ILogSink? _log;
        private readonly object _syncRoot = new object();

        private int FailureThreshold => _settings != null && _settings.CircuitBreakerFailureThreshold > 0
            ? _settings.CircuitBreakerFailureThreshold
            : RimMindDefaults.CircuitBreakerFailureThreshold;

        private TimeSpan OpenDuration => TimeSpan.FromSeconds(
            _settings != null && _settings.CircuitBreakerOpenDurationSec >= 0
            ? _settings.CircuitBreakerOpenDurationSec
            : RimMindDefaults.CircuitBreakerOpenDurationSec);

        private enum CircuitState { Closed, Open, HalfOpen }

        private sealed class CircuitBucket
        {
            public CircuitState State = CircuitState.Closed;
            public int ConsecutiveFailures;
            public DateTime OpenedAtUtc;
            public bool CanaryInProgress;
        }

        private readonly Dictionary<string, CircuitBucket> _buckets = new Dictionary<string, CircuitBucket>(StringComparer.Ordinal);
        private CircuitBucket _lastBucket = new CircuitBucket();

        internal string CurrentStateName { get { lock (_syncRoot) return _lastBucket.State.ToString(); } }
        internal int ConsecutiveFailuresCount { get { lock (_syncRoot) return _lastBucket.ConsecutiveFailures; } }
        internal bool IsCanaryInProgress { get { lock (_syncRoot) return _lastBucket.CanaryInProgress; } }

        internal string GetStateName(IAIClient client)
        {
            lock (_syncRoot)
            {
                return _buckets.TryGetValue(ResolveClientKey(client), out var bucket)
                    ? bucket.State.ToString()
                    : CircuitState.Closed.ToString();
            }
        }

        internal int GetConsecutiveFailuresCount(IAIClient client)
        {
            lock (_syncRoot)
            {
                return _buckets.TryGetValue(ResolveClientKey(client), out var bucket)
                    ? bucket.ConsecutiveFailures
                    : 0;
            }
        }

        public CircuitBreakerMiddleware(ICircuitBreakerSettings? settings = null, ILogSink? log = null)
        {
            _settings = settings;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            bool isCanary = false;
            CircuitBucket bucket;

            lock (_syncRoot)
            {
                bucket = GetOrCreateBucketLocked(context);
                if (bucket.State == CircuitState.Open)
                {
                    if (DateTime.UtcNow - bucket.OpenedAtUtc >= OpenDuration)
                    {
                        bucket.State = CircuitState.HalfOpen;
                        bucket.CanaryInProgress = false;
                        _log?.Message("[UnifiedCircuitBreaker] Transitioning to HalfOpen");
                    }
                    else
                    {
                        ShortCircuitOpen(context);
                        return;
                    }
                }

                if (bucket.State == CircuitState.HalfOpen)
                {
                    if (bucket.CanaryInProgress)
                    {
                        _log?.Warning("[UnifiedCircuitBreaker] HalfOpen canary already in progress, short-circuiting request");
                        ShortCircuitOpen(context);
                        return;
                    }

                    bucket.CanaryInProgress = true;
                    isCanary = true;
                    _log?.Message("[UnifiedCircuitBreaker] Allowing single canary probe through");
                }
            }

            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_syncRoot)
                {
                    bucket = GetOrCreateBucketLocked(context);
                    if (isCanary)
                    {
                        bucket.CanaryInProgress = false;
                    }
                }
                throw;
            }
            catch (Exception)
            {
                lock (_syncRoot)
                {
                    bucket = GetOrCreateBucketLocked(context);
                    OnFailureLocked(bucket, new RimMindError(RimMindErrorCode.ClientTransientFailure, "Downstream middleware threw exception"), isCanary);
                }
                throw;
            }

            lock (_syncRoot)
            {
                bucket = GetOrCreateBucketLocked(context);
                if (context.Result?.IsErr == true)
                {
                    OnFailureLocked(bucket, context.Result.Value.Error, isCanary);
                }
                else
                {
                    OnSuccessLocked(bucket, isCanary);
                }
            }
        }

        private CircuitBucket GetOrCreateBucketLocked(LlmRequestContext context)
        {
            string key = ResolveProviderKey(context);
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new CircuitBucket();
                _buckets[key] = bucket;
            }
            _lastBucket = bucket;
            return bucket;
        }

        private static string ResolveProviderKey(LlmRequestContext context)
        {
            if (context.Envelope?.Hints != null
                && context.Envelope.Hints.TryGetValue("ProviderId", out var providerId)
                && !string.IsNullOrWhiteSpace(providerId))
            {
                return providerId!;
            }

            if (context.Client != null)
            {
                return ResolveClientKey(context.Client);
            }

            return "default";
        }

        private static string ResolveClientKey(IAIClient client)
            => $"client:{client.GetType().FullName}:{RuntimeHelpers.GetHashCode(client)}";

        private void ShortCircuitOpen(LlmRequestContext context)
        {
            _log?.Warning("[UnifiedCircuitBreaker] Circuit open, short-circuiting request");
            context.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.CircuitOpen());
            context.ShortCircuit("circuit_open");
        }

        private void OnSuccessLocked(CircuitBucket bucket, bool isCanary)
        {
            bucket.ConsecutiveFailures = 0;
            bucket.State = CircuitState.Closed;
            if (isCanary)
            {
                bucket.CanaryInProgress = false;
            }
        }

        private void OnFailureLocked(CircuitBucket bucket, RimMindError? error, bool isCanary)
        {
            if (isCanary)
            {
                bucket.CanaryInProgress = false;
            }

            // Do NOT increment failures or trip circuit breaker on cancellation or non-transport/local errors
            if (error != null && (
                error.Code == RimMindErrorCode.Cancelled ||
                error.Code == RimMindErrorCode.ClientNotConfigured ||
                error.Code == RimMindErrorCode.PipelineShortCircuited ||
                error.Code == RimMindErrorCode.ContextBuildFailed))
            {
                return;
            }

            if (bucket.State == CircuitState.HalfOpen)
            {
                bucket.State = CircuitState.Open;
                bucket.OpenedAtUtc = DateTime.UtcNow;
                _log?.Warning("[UnifiedCircuitBreaker] Canary probe failed. Circuit reopening.");
                return;
            }

            bucket.ConsecutiveFailures++;
            if (bucket.ConsecutiveFailures >= FailureThreshold)
            {
                bucket.State = CircuitState.Open;
                bucket.OpenedAtUtc = DateTime.UtcNow;
                _log?.Warning($"[UnifiedCircuitBreaker] Circuit opened after {bucket.ConsecutiveFailures} consecutive failures");
            }
        }
    }
}
