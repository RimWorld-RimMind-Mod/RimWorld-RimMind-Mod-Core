using System.Collections.Generic;
using System.Linq;
using RimMind.Domain.Enums;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed record AgentListGroups(
        IReadOnlyList<AgentListItem> Active,
        IReadOnlyList<AgentListItem> Paused,
        IReadOnlyList<AgentListItem> PendingCreation,
        IReadOnlyList<AgentListItem> Other);

    public static class AgentListBuilder
    {
        public static AgentListGroups Build(
            IEnumerable<AgentListItem>? existingAgents,
            string? pendingSelectedPawnId,
            string? pendingSelectedPawnLabel)
        {
            var all = (existingAgents ?? Enumerable.Empty<AgentListItem>()).ToList();
            var pending = new List<AgentListItem>();

            if (!string.IsNullOrWhiteSpace(pendingSelectedPawnId)
                && all.All(a => a.Id != pendingSelectedPawnId))
            {
                pending.Add(AgentListItem.PendingPawn(
                    pendingSelectedPawnId,
                    pendingSelectedPawnLabel ?? pendingSelectedPawnId));
            }

            return new AgentListGroups(
                all.Where(a => a.State == AgentState.Active).ToList(),
                all.Where(a => a.State == AgentState.Paused).ToList(),
                pending,
                all.Where(a => a.State != AgentState.Active && a.State != AgentState.Paused).ToList());
        }
    }
}
