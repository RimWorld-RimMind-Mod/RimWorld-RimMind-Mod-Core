using System.Collections.Generic;
using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IPawnRecorder
    {
        IReadOnlyList<BehaviorRecord> History { get; }
        void Record(BehaviorRecord record);
        IReadOnlyList<BehaviorRecord> GetRecentHistory(int count = 10);
        float GetRecentSuccessRate(int count = 10);
    }
}
