namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IContextBudgetSettings
    {
        float ContextBudget { get; set; }
        int ContextBriefLimit { get; }
        int MaxCacheEntries { get; }
    }
}
