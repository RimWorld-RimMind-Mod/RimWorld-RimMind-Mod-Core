using RimMind.Core.Context;
using RimMind.Core.Extensions;

namespace RimMind.Core.Flywheel
{
    /// <summary>
    /// 内建 IParameterTuner，从 FlywheelParameterStore 读取参数并注入 BudgetSchedulerConfig。
    /// 在 FlywheelRuleEngine 的 ApplyAutoApplyMode 调谐循环中执行。
    /// </summary>
    public class FlywheelBuiltinTuner : IParameterTuner
    {
        public string TunerId => "FlywheelBuiltin";

        public void Tune(BudgetSchedulerConfig config)
        {
            var store = FlywheelParameterStore.Instance;
            if (store == null) return;

            config.W1 = store.Get("w1");
            config.W2 = store.Get("w2");
            config.Alpha = store.Get("Alpha");
            config.AlphaSmooth = store.Get("AlphaSmooth");
            config.PromoteThreshold = store.Get("PromoteThreshold");
            config.DemoteThreshold = store.Get("DemoteThreshold");
        }
    }
}
