using Verse;

namespace RimMind.Application.Common.Models.Agent
{
    // Preserved for future persistence support. Behavior history is currently runtime-only.
    /// <summary>
    /// Verse-serializable BehaviorRecord.
    /// Subclass in Presentation layer so Application layer stays Verse-free.
    /// </summary>
    public class SerializableBehaviorRecord : BehaviorRecord, IExposable
    {
        public SerializableBehaviorRecord() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Action, "action", "");
            Scribe_Values.Look(ref Reason, "reason", "");
            Scribe_Values.Look(ref Success, "success", false);
            Scribe_Values.Look(ref ResultReason, "resultReason", "");
            Scribe_Values.Look(ref GoalProgressDelta, "goalProgressDelta", 0f);
            Scribe_Values.Look(ref Timestamp, "timestamp", 0);
            Scribe_Values.Look(ref ActionEventId, "actionEventId", "");
            Scribe_Values.Look(ref DurationMs, "durationMs", 0);
        }
    }
}
