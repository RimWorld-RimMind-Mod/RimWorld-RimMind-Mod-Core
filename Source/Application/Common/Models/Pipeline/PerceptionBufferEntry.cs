namespace RimMind.Application.Common.Models.Pipeline
{
    public class PerceptionBufferEntry
    {
        public string PerceptionType = "";
        public string Content = "";
        public float Importance;
        public int Timestamp;
        public int PawnId;
        public string Source = "";
        public float Priority;
        public long TimestampTicks;
        public int Tick;
    }
}
