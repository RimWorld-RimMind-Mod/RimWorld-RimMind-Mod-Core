using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public class PerceptionBufferEntry
    {
        public string PerceptionType { get; set; } = "";
        public string Content { get; set; } = "";
        public float Importance { get; set; }
        public int Timestamp { get; set; }
        public int PawnId { get; set; }

        public string DedupKey => $"{PerceptionType}:{Content}";
    }

    public class PerceptionBuffer
    {
        private const int DefaultCapacity = 20;
        private readonly Queue<PerceptionBufferEntry> _entries = new Queue<PerceptionBufferEntry>();

        public int Capacity { get; }
        public IReadOnlyList<PerceptionBufferEntry> Entries => new List<PerceptionBufferEntry>(_entries);

        public PerceptionBuffer(int capacity = DefaultCapacity)
        {
            Capacity = capacity;
        }

        public void Add(PerceptionBufferEntry entry)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }

        public List<PerceptionBufferEntry> Flush()
        {
            var result = new List<PerceptionBufferEntry>(_entries);
            _entries.Clear();
            return result;
        }

        public void Clear() => _entries.Clear();
    }
}
