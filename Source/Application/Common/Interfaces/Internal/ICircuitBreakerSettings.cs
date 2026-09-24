namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface ICircuitBreakerSettings
    {
        int CircuitBreakerFailureThreshold { get; }
        int CircuitBreakerOpenDurationSec { get; }
    }
}
