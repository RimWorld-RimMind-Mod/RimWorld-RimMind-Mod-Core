namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IPawnThinker
    {
        void Tick();
        void ForceThink();
        bool IsThinking { get; }

        /// <summary>
        /// Whether the thinker should initiate a new think cycle.
        /// Used by PawnAgent to drive the workflow state machine.
        /// </summary>
        bool ShouldThink();

        /// <summary>
        /// Reset the thinking state (e.g., on Pause or workflow interruption).
        /// Clears _thinking flag and pending callback.
        /// </summary>
        void ResetThinking();
    }
}
