namespace RimMind.Application.Common.Models.Agent
{
    public sealed class BehaviorRecordDto
    {
        public string Action { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool Success { get; set; }
        public string ResultReason { get; set; } = "";
        public float GoalProgressDelta { get; set; }
        public int Timestamp { get; set; }
        public string ActionEventId { get; set; } = "";
        public int DurationMs { get; set; }
    }
}
