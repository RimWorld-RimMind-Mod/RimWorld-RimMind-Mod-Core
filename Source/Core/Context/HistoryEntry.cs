using Verse;

namespace RimMind.Core.Context
{
    public class HistoryEntry : IExposable
    {
        public string Role = null!;
        public string Content = null!;
        public int Tick;
        public string? Scenario;

        public HistoryEntry() { }

        public HistoryEntry(string role, string content, int tick, string? scenario = null)
        {
            Role = role;
            Content = content;
            Tick = tick;
            Scenario = scenario;
        }

        public void ExposeData()
        {
#pragma warning disable CS8601
            Scribe_Values.Look(ref Role, "role");
            Scribe_Values.Look(ref Content, "content");
#pragma warning restore CS8601
            Role ??= "";
            Content ??= "";
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Scenario, "scenario");
        }
    }
}
