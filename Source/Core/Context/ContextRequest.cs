using RimWorld;

namespace RimMind.Core.Context
{
    public class ContextRequest
    {
        public string NpcId = null!;
        public string Scenario = ScenarioIds.Dialogue;
        public float Budget = 0;
        public string? CurrentQuery;
        public string[]? ExcludeKeys;
        public int MaxTokens = 400;
        public float Temperature = 0.7f;
    }
}
