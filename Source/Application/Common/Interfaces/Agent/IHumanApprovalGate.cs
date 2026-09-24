using System;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IHumanApprovalGate
    {
        bool RequiresApproval(AgentDecision decision, RiskLevel riskLevel);
        void RequestApproval(AgentDecision decision, Action<bool> callback);
    }
}
