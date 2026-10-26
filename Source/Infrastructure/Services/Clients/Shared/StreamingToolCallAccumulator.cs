using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Reassembles OpenAI-style streaming <c>tool_calls</c> deltas. Providers send each tool call
    /// as fragments keyed by <c>index</c>: the first fragment usually carries <c>id</c>/<c>name</c>,
    /// later fragments carry pieces of <c>arguments</c>. The accumulated result is emitted through
    /// <see cref="ToolCallJsonNormalizer"/> so streaming and non-streaming responses expose the
    /// same <c>ToolCallsJson</c> array shape.
    /// </summary>
    internal sealed class StreamingToolCallAccumulator
    {
        private sealed class Slot
        {
            public string? Id;
            public string? Type;
            public string? Name;
            public readonly StringBuilder Arguments = new StringBuilder();
        }

        private readonly SortedDictionary<int, Slot> _slots = new SortedDictionary<int, Slot>();
        private int _lastIndex = -1;

        public bool HasAny => _slots.Count > 0;

        /// <summary>
        /// Appends one delta fragment. When the provider omits <paramref name="index"/>, a fragment
        /// carrying a new <paramref name="id"/> opens a new slot; otherwise it extends the last slot.
        /// </summary>
        public void Append(int? index, string? id, string? type, string? name, string? argumentsFragment)
        {
            int key = ResolveIndex(index, id);
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = new Slot();
                _slots[key] = slot;
            }
            _lastIndex = key;

            if (!string.IsNullOrEmpty(id) && string.IsNullOrEmpty(slot.Id))
                slot.Id = id;
            if (!string.IsNullOrEmpty(type) && string.IsNullOrEmpty(slot.Type))
                slot.Type = type;
            if (!string.IsNullOrEmpty(name))
            {
                if (string.IsNullOrEmpty(slot.Name))
                    slot.Name = name;
                else if (!string.Equals(slot.Name, name, System.StringComparison.Ordinal))
                    slot.Name += name;
            }
            if (!string.IsNullOrEmpty(argumentsFragment))
                slot.Arguments.Append(argumentsFragment);
        }

        private int ResolveIndex(int? index, string? id)
        {
            if (index.HasValue && index.Value >= 0)
                return index.Value;

            if (_lastIndex < 0)
                return 0;

            if (!string.IsNullOrEmpty(id) && _slots.TryGetValue(_lastIndex, out var last) &&
                !string.IsNullOrEmpty(last.Id) && last.Id != id)
            {
                return _slots.Keys.Max() + 1;
            }

            return _lastIndex;
        }

        /// <summary>Returns the normalized tool-call JSON array, or null when no tool call was streamed.</summary>
        public string? ToNormalizedJson()
        {
            if (_slots.Count == 0) return null;
            return ToolCallJsonNormalizer.Normalize(_slots.Values
                .Where(s => !string.IsNullOrEmpty(s.Name) || s.Arguments.Length > 0)
                .Select(s => new ToolCallEntry
                {
                    Id = s.Id ?? string.Empty,
                    Type = string.IsNullOrEmpty(s.Type) ? "function" : s.Type!,
                    FunctionName = s.Name,
                    FunctionArguments = s.Arguments.Length > 0 ? s.Arguments.ToString() : "{}",
                }));
        }
    }
}
