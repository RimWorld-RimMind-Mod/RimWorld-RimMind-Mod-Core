using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Tools
{
    public readonly struct ToolPolicyDecision
    {
        public AutonomyDomain? Domain { get; init; }
        public string? DomainId { get; init; }
        public RiskLevel Risk { get; init; }
        public bool RequiresApproval { get; init; }
    }

    public interface IToolPolicyClassifier
    {
        ToolPolicyDecision Classify(ToolCallArgs args);
    }

    public static class ToolPolicyClassifierExtensions
    {
        public static ToolPolicyDecision? ClassifyPolicy(this IToolRegistry? registry, string? toolId, ToolCallArgs? args = null)
        {
            if (registry == null || string.IsNullOrWhiteSpace(toolId))
                return null;

            var handler = registry.FindById(toolId!);
            if (handler == null)
                return null;

            if (args != null && handler is IToolPolicyClassifier classifier)
            {
                return classifier.Classify(args);
            }

            var manifest = handler.Definition?.Manifest ?? ToolManifest.Default;
            return new ToolPolicyDecision
            {
                Domain = manifest.DefaultDomain,
                DomainId = manifest.AutonomyDomainId ?? manifest.DefaultDomain?.ToString(),
                Risk = manifest.RiskLevel,
                RequiresApproval = manifest.RequiresApproval
            };
        }

        public static ToolPolicyDecision? Preflight(this IToolRegistry? registry, string? toolId, ToolCallArgs? args = null)
            => ClassifyPolicy(registry, toolId, args);
    }
}
