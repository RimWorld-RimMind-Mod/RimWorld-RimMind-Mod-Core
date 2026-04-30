using RimMind.Core.Context;

namespace RimMind.Core.Extensions
{
    public interface IParameterTuner
    {
        void Tune(BudgetSchedulerConfig config);
        string TunerId { get; }
    }
}
