namespace RimMind.Application.Common.Models.Agent
{
    public sealed class AgentLoopSnapshot
    {
        public AgentLoopSnapshot(
            int registeredPawnAgents,
            int registeredScopedAgents,
            int activeAgents,
            int pausedAgents,
            int dormantAgents,
            int terminatedAgents,
            int lastTick,
            int tickedAgents,
            int faultedAgents)
        {
            RegisteredPawnAgents = registeredPawnAgents;
            RegisteredScopedAgents = registeredScopedAgents;
            ActiveAgents = activeAgents;
            PausedAgents = pausedAgents;
            DormantAgents = dormantAgents;
            TerminatedAgents = terminatedAgents;
            LastTick = lastTick;
            TickedAgents = tickedAgents;
            FaultedAgents = faultedAgents;
        }

        public static AgentLoopSnapshot Empty { get; } = new(
            registeredPawnAgents: 0,
            registeredScopedAgents: 0,
            activeAgents: 0,
            pausedAgents: 0,
            dormantAgents: 0,
            terminatedAgents: 0,
            lastTick: -1,
            tickedAgents: 0,
            faultedAgents: 0);

        public int RegisteredPawnAgents { get; }
        public int RegisteredScopedAgents { get; }
        public int ActiveAgents { get; }
        public int PausedAgents { get; }
        public int DormantAgents { get; }
        public int TerminatedAgents { get; }
        public int LastTick { get; }
        public int TickedAgents { get; }
        public int FaultedAgents { get; }
    }
}
