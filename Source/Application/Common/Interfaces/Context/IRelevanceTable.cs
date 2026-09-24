using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IRelevanceTable
    {
        void Register(string scenarioId, string key, float relevance);
        void RegisterBatch(string scenarioId, Dictionary<string, float> entries);
        bool Unregister(string scenarioId, string key);
        float GetRelevance(string scenarioId, string key);
        void RegisterCoreRelevance();
        void Clear();
    }
}
