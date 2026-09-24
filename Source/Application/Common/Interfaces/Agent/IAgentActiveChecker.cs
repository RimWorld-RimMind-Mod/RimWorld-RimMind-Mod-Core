namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IAgentActiveChecker
    {
        bool IsAgentActive(string pawnThingId);
    }
}
