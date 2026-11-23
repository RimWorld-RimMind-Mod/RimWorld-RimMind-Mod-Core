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
        private readonly Dictionary<string, AutonomyLevel> _customDomainLevels = new(StringComparer.OrdinalIgnoreCase);

        public AgentAutonomyPolicy()
        {
            SetDefaultPreset();
        }

        public AutonomyLevel GetLevel(AutonomyDomain domain)
        {
            return _domainLevels.TryGetValue(domain, out var level) ? level : AutonomyLevel.RequireApproval;
        }

        public AutonomyLevel GetLevel(string? domainId)
        {
            if (string.IsNullOrWhiteSpace(domainId))
                return AutonomyLevel.RequireApproval;
            if (Enum.TryParse<AutonomyDomain>(domainId!.Trim(), ignoreCase: true, out var builtIn))
                return GetLevel(builtIn);
            return _customDomainLevels.TryGetValue(domainId.Trim(), out var custom) ? custom : AutonomyLevel.RequireApproval;
        }

        public void SetLevel(AutonomyDomain domain, AutonomyLevel level)
        {
            _domainLevels[domain] = level;
        }

        public void SetLevel(string domainId, AutonomyLevel level)
        {
            if (string.IsNullOrWhiteSpace(domainId)) return;
            var trimmed = domainId.Trim();
            if (Enum.TryParse<AutonomyDomain>(trimmed, ignoreCase: true, out var builtIn))
            {
                _domainLevels[builtIn] = level;
                return;
            }
            _customDomainLevels[trimmed] = level;
        }

        public bool CanExecuteAutonomous(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.FullAutonomous;
        }

        public bool CanExecuteAutonomous(string? domainId)
        {
            return GetLevel(domainId) == AutonomyLevel.FullAutonomous;
        }

        public bool RequiresApproval(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.RequireApproval;
        }

        public bool RequiresApproval(string? domainId)
        {
            return GetLevel(domainId) == AutonomyLevel.RequireApproval;
        }

        public bool IsDisabled(AutonomyDomain domain)
        {
            return GetLevel(domain) == AutonomyLevel.Disabled;
        }

        public bool IsDisabled(string? domainId)
        {
            return GetLevel(domainId) == AutonomyLevel.Disabled;
        }

        public void SetDefaultPreset()
        {
            _domainLevels[AutonomyDomain.EmergencySurvival] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.ResourceMarking] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.WorktableBills] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.BlueprintPlacement] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.HighRiskOperations] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.WorkPriorityModification] = AutonomyLevel.Disabled;
        }

        public void ApplyWorkOnlyNoBlueprintsPreset()
        {
            _domainLevels[AutonomyDomain.EmergencySurvival] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.ResourceMarking] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.WorktableBills] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.BlueprintPlacement] = AutonomyLevel.Disabled;
            _domainLevels[AutonomyDomain.HighRiskOperations] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.WorkPriorityModification] = AutonomyLevel.Disabled;
        }

        public void ApplyFullAutonomousPreset()
        {
            _domainLevels[AutonomyDomain.EmergencySurvival] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.ResourceMarking] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.WorktableBills] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.BlueprintPlacement] = AutonomyLevel.FullAutonomous;
            _domainLevels[AutonomyDomain.HighRiskOperations] = AutonomyLevel.RequireApproval;
            _domainLevels[AutonomyDomain.WorkPriorityModification] = AutonomyLevel.FullAutonomous;
        }

        public void CopyFrom(AgentAutonomyPolicy? other)
        {
            if (other == null) return;
            foreach (AutonomyDomain domain in Enum.GetValues(typeof(AutonomyDomain)))
            {
                _domainLevels[domain] = other.GetLevel(domain);
            }
            _customDomainLevels.Clear();
            foreach (var kvp in other._customDomainLevels)
            {
                _customDomainLevels[kvp.Key] = kvp.Value;
            }
        }

        public AgentAutonomyPolicy Clone()
        {
            var clone = new AgentAutonomyPolicy();
            foreach (var kvp in _domainLevels)
            {
                clone._domainLevels[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in _customDomainLevels)
            {
                clone._customDomainLevels[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        public static AgentAutonomyPolicy CreateDefault() => new AgentAutonomyPolicy();
    }
}
