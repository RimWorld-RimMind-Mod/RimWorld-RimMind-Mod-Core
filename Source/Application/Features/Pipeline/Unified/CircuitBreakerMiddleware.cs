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

        private int FailureThreshold => _settings != null && _settings.CircuitBreakerFailureThreshold > 0
            ? _settings.CircuitBreakerFailureThreshold
            : RimMindDefaults.CircuitBreakerFailureThreshold;

        private TimeSpan OpenDuration => TimeSpan.FromSeconds(
            _settings != null && _settings.CircuitBreakerOpenDurationSec > 0
            ? _settings.CircuitBreakerOpenDurationSec
            : RimMindDefaults.CircuitBreakerOpenDurationSec);

        private enum CircuitState { Closed, Open, HalfOpen }

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTime _openedAtUtc;

        public CircuitBreakerMiddleware(ICircuitBreakerSettings? settings = null, ILogSink? log = null)
        {
            _settings = settings;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            switch (_state)
            {
                case CircuitState.Open:
                    if (DateTime.UtcNow - _openedAtUtc >= OpenDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _log?.Message("[UnifiedCircuitBreaker] Transitioning to HalfOpen");
                    }
                    else
                    {
                        _log?.Warning("[UnifiedCircuitBreaker] Circuit open, short-circuiting request");
                        context.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.CircuitOpen());
                        context.ShortCircuit("circuit_open");
                        return;
                    }
                    break;
            }

            await next(context).ConfigureAwait(false);

            if (context.Result?.IsErr == true)
            {
                OnFailure();
            }
            else
            {
                OnSuccess();
            }
        }

        private void OnSuccess()
        {
            _consecutiveFailures = 0;
            _state = CircuitState.Closed;
        }

        private void OnFailure()
        {
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
