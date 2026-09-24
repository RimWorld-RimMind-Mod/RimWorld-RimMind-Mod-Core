using System.Collections.Generic;
using RimMind.Application.Common.Models.Memory;

namespace RimMind.Application.Common.Interfaces.Memory
{
    /// <summary>Safe default when RimMind-Memory is absent or has not registered yet.</summary>
    public sealed class NullMemoryBridge : IMemoryBridge
    {
        public bool AddPawnMemory(string content, MemoryKind kind, int tick, float importance, string? pawnId) => false;
        public bool AddNarratorMemory(string content, int tick, float importance) => false;
        public IReadOnlyList<NarratorMemoryEntry> GetRecentNarrations(int maxEntries)
            => System.Array.Empty<NarratorMemoryEntry>();
    }
}
