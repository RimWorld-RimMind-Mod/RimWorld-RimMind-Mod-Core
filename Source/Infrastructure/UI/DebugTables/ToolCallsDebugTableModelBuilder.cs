using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Debug;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public sealed class ToolCallsDebugTableModelBuilder : IDebugTableModelBuilder
    {
        private IAIRequestTraceLog? _log;
        private long _cachedGeneration = long.MinValue;
        private long _cachedRevision = long.MinValue;
        private DebugTableModel? _cachedModel;

        public ToolCallsDebugTableModelBuilder(IAIRequestTraceLog? log = null)
        {
            _log = log;
        }

        public void Bind(IAIRequestTraceLog? log, long generation)
        {
            if (_cachedGeneration == generation && ReferenceEquals(_log, log))
                return;
            _log = log;
            _cachedGeneration = generation;
            _cachedRevision = long.MinValue;
            _cachedModel = null;
        }

        public DebugTableModel Build()
        {
            if (_log == null)
                return _cachedModel ??= Build(System.Array.Empty<AIRequestTraceEntry>());

            long revision = _log.Revision;
            if (_cachedModel != null && revision == _cachedRevision)
                return _cachedModel;

            _cachedModel = Build(_log.Entries);
            _cachedRevision = revision;
            return _cachedModel;
        }

        public static DebugTableModel Build(IReadOnlyList<AIRequestTraceEntry> entries)
        {
            return new DebugTableModel(
                "RimMind.UI.Hub.Tab.ToolCalls".Translate(),
                entries.SelectMany(entry => entry.ToolCalls.Select((toolCall, index) => ToRow(entry, toolCall, index))));
        }

        private static DebugTableRow ToRow(AIRequestTraceEntry entry, AIRequestToolCallTrace toolCall, int index)
        {
            string duration = entry.ElapsedMs > 0 ? entry.ElapsedMs + " ms" : string.Empty;
            string summary = toolCall.Succeeded
                ? string.Empty
                : toolCall.Error ?? string.Empty;
            string id = string.IsNullOrWhiteSpace(toolCall.ToolCallId)
                ? entry.RequestId + ":tool:" + index
                : toolCall.ToolCallId;

            return DebugTableRow.Create(
                id,
                toolCall.Succeeded ? DebugTableStatus.Completed : DebugTableStatus.Failed,
                string.Empty,
                entry.Source,
                string.Empty,
                DebugTableText.Preview(toolCall.ToolName),
                entry.Model,
                DebugTableText.Preview(summary),
                duration);
        }
    }
}
