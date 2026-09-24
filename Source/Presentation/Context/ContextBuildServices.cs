using RimMind.Application.Common.Interfaces.Context;

namespace RimMind.Presentation.Context
{
    public class ContextBuildServices
    {
        public IContextCacheManager CacheManager { get; }
        public IContextDiffTracker DiffTracker { get; }
        public IContextLayerBuilder LayerBuilder { get; }
        public IBudgetScheduler BudgetScheduler { get; }

        public ContextBuildServices(
            IContextCacheManager cacheManager,
            IContextDiffTracker diffTracker,
            IContextLayerBuilder layerBuilder,
            IBudgetScheduler budgetScheduler)
        {
            CacheManager = cacheManager;
            DiffTracker = diffTracker;
            LayerBuilder = layerBuilder;
            BudgetScheduler = budgetScheduler;
        }
    }
}
