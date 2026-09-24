using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Features.Agent
{
    public interface IAgenticLoopService
    {
        int MaxDepth { get; }
        bool ShouldContinue(AgentDecision decision, int currentDepth);
        LoopResult Evaluate(AgentDecision decision, int currentDepth);
    }

    public sealed record LoopResult(bool ShouldContinue, AgentDecision? FinalDecision = null, string? Reason = null)
    {
        public static LoopResult Continue(string reason) => new(true, Reason: reason);
        public static LoopResult Stop(AgentDecision decision, string? reason = null) => new(false, decision, reason);
    }
}
