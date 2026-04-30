namespace RimMind.Core.Context
{
    public class BudgetSchedulerConfig
    {
        public float W1 = 0.4f;
        public float W2 = 0.6f;
        public float Alpha = 0.01f;
        public float AlphaSmooth = 0.7f;
        public float PromoteThreshold = 0.8f;
        public float DemoteThreshold = 0.2f;
    }
}
