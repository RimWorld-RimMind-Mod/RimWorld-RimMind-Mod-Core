using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Debug;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public sealed class AIRequestsDebugTableModelBuilder : IDebugTableModelBuilder
    {
        private readonly IAIRequestTraceLog? _log;

        public AIRequestsDebugTableModelBuilder(IAIRequestTraceLog? log)
        {
            _log = log;
        }

        public DebugTableModel Build()
            => Build(_log?.Entries ?? System.Array.Empty<AIRequestTraceEntry>());

        public static DebugTableModel Build(IReadOnlyList<AIRequestTraceEntry> entries)
        {
            return new DebugTableModel(
                "RimMind.UI.Hub.Tab.AIRequests".Translate(),
                entries.Select(ToRow));
        }

        private static DebugTableRow ToRow(AIRequestTraceEntry entry)
        {
            string summary = entry.State == AIRequestTraceState.Failed && !string.IsNullOrWhiteSpace(entry.Error)
                ? entry.Error!
                : !string.IsNullOrWhiteSpace(entry.Response)
                    ? entry.Response
                    : entry.UserPrompt;

            string duration = entry.ElapsedMs > 0 ? entry.ElapsedMs + " ms" : string.Empty;
            return DebugTableRow.Create(
                entry.RequestId,
                StatusFor(entry.State),
                string.Empty,
                entry.Source,
                string.Empty,
                entry.ToolCalls.Count > 0 ? entry.ToolCalls[0].ToolName : string.Empty,
                entry.Model,
                DebugTableText.Preview(summary),
                duration);
        }

        private static DebugTableStatus StatusFor(AIRequestTraceState state)
            => state switch
            {
                AIRequestTraceState.Running => DebugTableStatus.Streaming,
                AIRequestTraceState.Completed => DebugTableStatus.Completed,
                AIRequestTraceState.Failed => DebugTableStatus.Failed,
                _ => DebugTableStatus.Waiting
            };
    }
}
