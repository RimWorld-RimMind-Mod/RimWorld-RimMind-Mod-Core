using System.Collections.Generic;
using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IStrategyOptimizer
    {
        List<KeyValuePair<string, float>> GetTopN(int n);
        void AdjustWeight(string action, float delta);
        List<StructuredTool> GetWeightedTools(List<StructuredTool> tools);
        void DecayAll();
    }
}
