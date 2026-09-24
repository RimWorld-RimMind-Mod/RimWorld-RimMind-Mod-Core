namespace RimMind.Application.Common.Models.Agent
{
    public class BehaviorRecord
    {
        public string Action = "";
        public string Reason = "";
        public bool Success;
        public string ResultReason = "";
        public float GoalProgressDelta;
        public int Timestamp;
        public string ActionEventId = "";
        public int DurationMs;

        public BehaviorRecord() { }

        public override string ToString() => $"BehaviorRecord({Action}, Success={Success}, T={Timestamp})";
    }
}
