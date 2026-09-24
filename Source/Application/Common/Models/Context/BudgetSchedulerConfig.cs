namespace RimMind.Application.Common.Models.Context
{
    public class BudgetSchedulerConfig
    {
        // 7-dimension weights
        public float W1 = 0.30f;   // Priority
        public float W2 = 0.25f;   // SceneRelevance
        public float W3 = 0.15f;   // QuerySimilarity
        public float W4 = 0.10f;   // Recency
        public float W5 = 0.15f;   // UseFeedback
        public float W6 = 0.05f;   // CooldownPenalty coefficient

        // Time parameters
        public int RecencyHalflife = 30000;  // ticks (~8 min half-life)
        public int CooldownWindow = 5000;    // ticks (~83 sec cooldown window)

        // Legacy fields (kept for backward compatibility)
        public float Alpha = 0.01f;
        public float AlphaSmooth = 0.7f;
        public float PromoteThreshold = 0.8f;
        public float DemoteThreshold = 0.2f;
        public float ContextBudget = 1.0f;
        public int MaxCacheEntries = RimMindDefaults.MaxCacheEntries;
    }
}
