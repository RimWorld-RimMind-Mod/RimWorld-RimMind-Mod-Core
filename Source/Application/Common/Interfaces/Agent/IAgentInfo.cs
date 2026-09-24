using System.Collections.Generic;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IAgentInfo
    {
        AgentState State { get; }
        string NpcId { get; }
        string Label { get; }
        int? LastThinkTick { get; set; }
        int GoalCount { get; }
        IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10);
        float GetRecentSuccessRate(int count = 10);
    }
}
