using RimMind.Domain.Enums;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed record AgentListItem(
        string Id,
        string Label,
        AgentState State,
        bool IsPendingCreation,
        AgentLifecycleGroup Group,
        string ScopeLabel,
        string StatusSummary)
    {
        public AgentListItem(
            string id,
            string label,
            AgentState state,
            bool isPendingCreation)
            : this(
                id,
                label,
                state,
                isPendingCreation,
                ResolveGroup(state, isPendingCreation),
                "Pawn",
                ResolveStatusSummary(state, isPendingCreation, errorSummary: null))
        {
        }

        public static AgentListItem ExistingPawn(string id, string label, AgentState state)
            => new(
                id,
                label,
                state,
                IsPendingCreation: false,
                ResolveGroup(state, isPendingCreation: false),
                ScopeLabel: "Pawn",
                StatusSummary: ResolveStatusSummary(state, isPendingCreation: false, errorSummary: null));

        public static AgentListItem PendingPawn(string id, string label)
            => new(
                id,
                label,
                AgentState.Dormant,
                IsPendingCreation: true,
                AgentLifecycleGroup.Pending,
                ScopeLabel: "Pawn",
                StatusSummary: "Pending creation");

        public static AgentListItem ErrorPawn(string id, string label, string statusSummary)
            => new(
                id,
                label,
                AgentState.Terminated,
                IsPendingCreation: false,
                AgentLifecycleGroup.Error,
                ScopeLabel: "Pawn",
                StatusSummary: string.IsNullOrWhiteSpace(statusSummary) ? "Error" : statusSummary);

        private static AgentLifecycleGroup ResolveGroup(AgentState state, bool isPendingCreation)
        {
            if (isPendingCreation)
                return AgentLifecycleGroup.Pending;

            return state switch
            {
                AgentState.Active => AgentLifecycleGroup.Active,
                AgentState.Paused => AgentLifecycleGroup.Paused,
                AgentState.Terminated => AgentLifecycleGroup.Error,
                _ => AgentLifecycleGroup.Pending
            };
        }

        private static string ResolveStatusSummary(
            AgentState state,
            bool isPendingCreation,
            string? errorSummary)
        {
            if (!string.IsNullOrWhiteSpace(errorSummary))
                return errorSummary;

            if (isPendingCreation)
                return "Pending creation";

            return state.ToString();
        }
    }
}
