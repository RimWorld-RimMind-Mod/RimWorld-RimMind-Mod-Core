using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IBudgetScheduler
    {
        BudgetAllocation Schedule(List<KeyMeta> keys, string scenarioId, float budget, string? currentQuery);
        BudgetAllocation ScheduleWithContext(List<KeyMeta> keys, ScoringContext sctx, float budget);
        void OnKeyUpdated(KeyMeta key);
        void Calibrate(List<KeyMeta> keys);
        void SetConfig(BudgetSchedulerConfig? config);
        BudgetSchedulerConfig GetConfig();
    }
}
