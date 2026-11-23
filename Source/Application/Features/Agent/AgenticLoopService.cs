using RimMind.Application.Common.Models;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Features.Agent
{
    public sealed record LoopResult(bool ShouldContinue, AgentDecision? FinalDecision = null, string? Reason = null)
    {
        public static LoopResult Continue(string reason) => new(true, Reason: reason);
        public static LoopResult Stop(AgentDecision decision, string? reason = null) => new(false, decision, reason);
    }

    public class AgenticLoopService
    {
        public AgenticLoopService(int maxDepth = RimMindDefaults.DefaultMaxToolCallDepth)
        {
            MaxDepth = maxDepth;
        }

        public int MaxDepth { get; }

        public bool ShouldContinue(AgentDecision decision, int currentDepth)
        {
            if (decision == null) return false;
            if (!decision.WantsMoreToolCalls) return false;
            if (currentDepth + 1 >= MaxDepth) return false;
            return true;
        }

        public LoopResult Evaluate(AgentDecision decision, int currentDepth)
        {
            if (!ShouldContinue(decision, currentDepth))
            {
                return LoopResult.Stop(decision!, "Loop termination: no more tool calls requested or max depth reached");
            }

            return LoopResult.Continue($"Tool call round {currentDepth + 1}");
        }
    }
}
