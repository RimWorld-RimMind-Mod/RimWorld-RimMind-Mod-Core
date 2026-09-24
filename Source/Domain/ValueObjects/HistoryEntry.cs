namespace RimMind.Domain.ValueObjects
{
    public class HistoryEntry
    {
        public string Role = "";
        public string Content = "";
        public int Tick;
        public string? Scenario;
        public string? TurnId;
        public bool IsPending;

        public HistoryEntry() { }

        public HistoryEntry(
            string role,
            string content,
            int tick,
            string? scenario = null,
            string? turnId = null,
            bool isPending = false)
        {
            Role = role;
            Content = content;
            Tick = tick;
            Scenario = scenario;
            TurnId = turnId;
            IsPending = isPending;
        }
    }
}
