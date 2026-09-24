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
            _entries.Add(entry);
            while (_entries.Count > MaxCapacity)
                _entries.RemoveAt(0);
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
