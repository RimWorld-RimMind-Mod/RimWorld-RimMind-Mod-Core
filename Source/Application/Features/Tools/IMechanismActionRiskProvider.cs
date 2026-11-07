using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Optional metadata a game mechanism can implement when a single write operation hosts actions of
    /// different risk (for example <c>pawn.job</c> Set covers both <c>force_rest</c> and <c>arrest_pawn</c>).
    /// <see cref="ToolManifestRiskResolver"/> escalates the operation risk to the declared action risk.
    /// </summary>
    public interface IMechanismActionRiskProvider
    {
        /// <summary>
        /// Returns the risk of <paramref name="action"/>, or null to use the operation's risk.
        /// </summary>
        MechanismRisk? GetRiskForAction(string action);
    }
}
