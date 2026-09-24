namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Agent identity interface located in Application/Common/Interfaces so sub-mods can reference it
    /// via 1_RimMindApplication.dll without depending on the Presentation layer or Verse.
    /// The IExposable implementation lives in the Presentation layer.
    /// </summary>
    public interface IAgentIdentity
    {
        string NpcId { get; }
        int PawnId { get; }
        string DisplayName { get; }
        System.Collections.Generic.List<string> Motivations { get; }
        System.Collections.Generic.List<string> PersonalityTraits { get; }
        System.Collections.Generic.List<string> CoreValues { get; }
    }
}
