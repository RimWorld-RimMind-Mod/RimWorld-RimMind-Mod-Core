using RimMind.Domain.Agent.Modes;
using RimMind.Application.Common.Interfaces.Tools;

namespace RimMind.Application.Features.Agent
{
    public sealed record ValidationResult(bool IsValid, string Reason = "")
    {
        public static ValidationResult Ok() => new(true);
        public static ValidationResult Fail(string reason) => new(false, reason);
    }

    public interface IDecisionValidator
    {
        ValidationResult Validate(AgentDecision decision, IToolRegistry toolRegistry);
    }
}
