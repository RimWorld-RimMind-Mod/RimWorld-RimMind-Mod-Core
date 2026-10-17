using System;
using System.Collections.Generic;

namespace RimMind.Domain.Agent.Policies
{
    /// <summary>
    /// Fine-grained tri-state autonomy permission policy for a colonist agent.
    /// </summary>
    public sealed class AgentAutonomyPolicy
    {
        private readonly Dictionary<AutonomyDomain, AutonomyLevel> _domainLevels = new();

        public AgentAutonomyPolicy()
        {
            SetDefaultPreset();
        }

        public AutonomyLevel GetLevel(AutonomyDomain domain)
        {
            return _domainLevels.TryGetValue(domain, out var level) ? level : AutonomyLevel.RequireApproval;
        }

        public void SetLevel(AutonomyDomain domain, AutonomyLevel level)
        {
            _domainLevels[domain] = level;
        }

        public bool CanExecuteAutonomous(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.FullAutonomous;
        }

        public bool RequiresApproval(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.RequireApproval;
        }

        public bool IsDisabled(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.Disabled;
        }

        public void SetDefaultPreset()
        {
            _domainLevels[AutonomyDomain.EmergencySurvival] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.ResourceMarking] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.WorktableBills] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.BlueprintPlacement] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.HighRiskOperations] = AutonomyLevel.RequireApproval;
        }

        public AgentAutonomyPolicy Clone()
        {
            var clone = new AgentAutonomyPolicy();
            foreach (var kvp in _domainLevels)
            {
                clone._domainLevels[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        public static AgentAutonomyPolicy CreateDefault() => new AgentAutonomyPolicy();
    }
}
