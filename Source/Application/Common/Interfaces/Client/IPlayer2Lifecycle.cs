using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Client
{
    /// <summary>
    /// Abstraction for Player2 client status and lifecycle management.
    /// Decouples Presentation layer from Infrastructure.Player2Client static methods.
    /// </summary>
    public interface IPlayer2Lifecycle
    {
        void StopHealthCheck();
        void CheckStatusAndNotify();
        void RefreshBalance();
        float CachedBalance { get; }
        bool IsAvailable { get; }
    }
}
