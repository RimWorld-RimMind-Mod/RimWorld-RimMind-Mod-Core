namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IProactiveBehaviorOrchestrator
    {
        void ExecuteReflection(IAgentInfo agent);
        void ExecutePlanning(IAgentInfo agent);
        void ExecuteDream(IAgentInfo agent);
        void ExecuteTraitEvolution(IAgentInfo agent);
    }
}
