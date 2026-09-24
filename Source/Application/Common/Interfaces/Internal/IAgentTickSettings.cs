namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IAgentTickSettings : IAgentAutonomySettings
    {
        int AgentTickInterval { get; }
        int BehaviorHistoryMax { get; set; }
        int ThinkCooldownTicks { get; }
        int MaxToolCallDepth { get; }
        int DefaultModCooldownTicks { get; set; }
        float ActivityFrequencyScale { get; set; }
        bool AutoActivateColonistAgents { get; set; }
    }
}
