namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IScopedAgent : IAgentControl
    {
        string ScopeId { get; }
        string ScopeType { get; }
        int? MapId { get; }
    }
}
