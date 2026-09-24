using System.Collections.Generic;
using RimMind.Application.Common.Models.Flywheel;

namespace RimMind.Application.Common.Interfaces.Flywheel
{
    public interface IFlywheelRuleEngine
    {
        void Analyze(List<TelemetryRecord> records);
        List<ParameterRecommendation> Evaluate(Dictionary<string, float> metrics);
    }
}
