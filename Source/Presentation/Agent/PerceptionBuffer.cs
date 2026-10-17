using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Presentation.Agent
{
    public class PerceptionBuffer : IPerceptionBuffer
    {
        private readonly List<PerceptionBufferEntry> _entries = new List<PerceptionBufferEntry>();
        private const int MaxCapacity = RimMindDefaults.PerceptionBufferCapacity;

        public IReadOnlyList<PerceptionBufferEntry> Entries => _entries;
        public int Count => _entries.Count;

        public void Add(PerceptionBufferEntry entry)
        {
            if (entry == null) return;

            string entrySemanticKey = GetSemanticDeduplicationKey(entry.Content);

            // De-duplicate: If an entry with the same PerceptionType, PawnId, and semantic metric key already exists, refresh it in place
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var existing = _entries[i];
                if (existing.PawnId == entry.PawnId &&
                    existing.PerceptionType == entry.PerceptionType &&
                    string.Equals(GetSemanticDeduplicationKey(existing.Content), entrySemanticKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    existing.Content = entry.Content;
                    existing.Importance = entry.Importance;
                    existing.Priority = entry.Priority;
                    existing.Timestamp = entry.Timestamp;
                    existing.TimestampTicks = entry.TimestampTicks;
                    existing.Tick = entry.Tick;
                    return;
                }
            }

            _entries.Add(entry);
            while (_entries.Count > MaxCapacity)
                _entries.RemoveAt(0);
        }

        public static string GetSemanticDeduplicationKey(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            string trimmed = content!.Trim();

            // e.g. "Need: Food at 74%" -> "Need: Food"
            if (trimmed.StartsWith("Need:", System.StringComparison.OrdinalIgnoreCase))
            {
                int atIdx = trimmed.IndexOf(" at ", System.StringComparison.OrdinalIgnoreCase);
                return atIdx > 0 ? trimmed.Substring(0, atIdx).Trim() : trimmed;
            }

            // e.g. "Mood: 65% (Content)" -> "Mood"
            if (trimmed.StartsWith("Mood:", System.StringComparison.OrdinalIgnoreCase))
                return "Mood";

            // e.g. "Environment: Clear, 21°C" -> "Environment"
            if (trimmed.StartsWith("Environment:", System.StringComparison.OrdinalIgnoreCase))
                return "Environment";

            // Generic "<Metric> at <number>%" pattern
            int genericAtIdx = trimmed.LastIndexOf(" at ", System.StringComparison.OrdinalIgnoreCase);
            if (genericAtIdx > 0 && genericAtIdx + 4 < trimmed.Length)
            {
                char c = trimmed[genericAtIdx + 4];
                if (char.IsDigit(c) || c == '-' || c == '+')
                    return trimmed.Substring(0, genericAtIdx).Trim();
            }

            return trimmed;
        }

        public List<PerceptionBufferEntry> Flush()
        {
            var result = new List<PerceptionBufferEntry>(_entries);
            _entries.Clear();
            return result;
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
