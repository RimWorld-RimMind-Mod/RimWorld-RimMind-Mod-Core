using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
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

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTime _openedAtUtc;
        private bool _canaryInProgress;

        internal string CurrentStateName { get { lock (_syncRoot) return _state.ToString(); } }
        internal int ConsecutiveFailuresCount { get { lock (_syncRoot) return _consecutiveFailures; } }
        internal bool IsCanaryInProgress { get { lock (_syncRoot) return _canaryInProgress; } }

        public CircuitBreakerMiddleware(ICircuitBreakerSettings? settings = null, ILogSink? log = null)
        {
            _settings = settings;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            bool isCanary = false;

            lock (_syncRoot)
            {
                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _openedAtUtc >= OpenDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _canaryInProgress = false;
                        _log?.Message("[UnifiedCircuitBreaker] Transitioning to HalfOpen");
                    }
                    else
                    {
                        ShortCircuitOpen(context);
                        return;
                    }
                }

                if (_state == CircuitState.HalfOpen)
                {
                    if (_canaryInProgress)
                    {
                        _log?.Warning("[UnifiedCircuitBreaker] HalfOpen canary already in progress, short-circuiting request");
                        ShortCircuitOpen(context);
                        return;
                    }

                    _canaryInProgress = true;
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
                    if (isCanary)
                    {
                        _canaryInProgress = false;
                    }
                }
                throw;
            }
            catch (Exception)
            {
                lock (_syncRoot)
                {
                    OnFailureLocked(new RimMindError(RimMindErrorCode.ClientTransientFailure, "Downstream middleware threw exception"), isCanary);
                }
                throw;
            }

            lock (_syncRoot)
            {
                if (context.Result?.IsErr == true)
                {
                    OnFailureLocked(context.Result.Value.Error, isCanary);
                }
                else
                {
                    OnSuccessLocked(isCanary);
                }
            }
        }

        private void ShortCircuitOpen(LlmRequestContext context)
        {
            _log?.Warning("[UnifiedCircuitBreaker] Circuit open, short-circuiting request");
            context.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.CircuitOpen());
            context.ShortCircuit("circuit_open");
        }

        private void OnSuccessLocked(bool isCanary)
        {
            _consecutiveFailures = 0;
            _state = CircuitState.Closed;
            if (isCanary)
            {
                _canaryInProgress = false;
            }
        }

        private void OnFailureLocked(RimMindError? error, bool isCanary)
        {
            if (isCanary)
            {
                _canaryInProgress = false;
            }

            // In OnFailure(), do NOT increment failures or trip circuit breaker if error is RimMindErrorCode.Cancelled
            if (error != null && error.Code == RimMindErrorCode.Cancelled)
            {
                return;
            }

            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Open;
                _openedAtUtc = DateTime.UtcNow;
                _log?.Warning("[UnifiedCircuitBreaker] Canary probe failed. Circuit reopening.");
                return;
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureThreshold)
            {
                _state = CircuitState.Open;
                _openedAtUtc = DateTime.UtcNow;
                _log?.Warning($"[UnifiedCircuitBreaker] Circuit opened after {_consecutiveFailures} consecutive failures");
            }
        }
    }
}
