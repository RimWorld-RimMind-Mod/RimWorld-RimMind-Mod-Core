namespace RimMind.Domain.Agent.Modes
{
    public sealed record AgentDecision(
        string ActionIntent = "",
        string Reason = "",
        string? TargetPawnId = null,
        string? Param = null,
        string? ToolCallId = null,
        bool WantsMoreToolCalls = false,
        int ToolCallRound = 0);
}
