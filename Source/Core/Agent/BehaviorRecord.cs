using Verse;

namespace RimMind.Core.Agent
{
    public class BehaviorRecord : IExposable
    {
        public string Action = "";
        public string Reason = "";
        public bool Success = true;
        public string ResultReason = "";
        public string? ActionEventId;
        public float GoalProgressDelta;
        public int Timestamp;

        public BehaviorRecord() { }

        public BehaviorRecord(string action, string reason, bool success, string resultReason = "", float goalProgressDelta = 0f)
        {
            Action = action ?? "";
            Reason = reason ?? "";
            Success = success;
            ResultReason = resultReason ?? "";
            ActionEventId = null;
            GoalProgressDelta = goalProgressDelta;
            Timestamp = Find.TickManager?.TicksGame ?? 0;
        }

        public void ExposeData()
        {
#pragma warning disable CS8601
            Scribe_Values.Look(ref Action, "action");
            Scribe_Values.Look(ref Reason, "reason");
            Scribe_Values.Look(ref Success, "success", true);
            Scribe_Values.Look(ref ResultReason, "resultReason");
#pragma warning restore CS8601
            Action ??= "";
            Reason ??= "";
            ResultReason ??= "";
            Scribe_Values.Look(ref ActionEventId, "actionEventId");
            Scribe_Values.Look(ref GoalProgressDelta, "goalProgressDelta");
            Scribe_Values.Look(ref Timestamp, "timestamp");
        }
    }
}
