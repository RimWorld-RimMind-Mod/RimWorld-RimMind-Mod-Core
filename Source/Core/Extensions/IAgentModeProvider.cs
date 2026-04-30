using Verse;

namespace RimMind.Core.Extensions
{
    public interface IAgentModeProvider
    {
        bool IsAgentControlled(Pawn pawn);
        string ProviderId { get; }
    }
}
