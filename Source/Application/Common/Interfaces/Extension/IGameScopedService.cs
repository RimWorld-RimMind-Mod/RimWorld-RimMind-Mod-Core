namespace RimMind.Application.Common.Interfaces.Extension;

/// <summary>
/// Open extension point for services that participate in the per-game lifecycle
/// (notified when a game is started/loaded and when a game is stopped/exited to main menu).
/// </summary>
public interface IGameScopedService : IExtension
{
    void OnGameStarted();
    void OnGameStopped();
}
