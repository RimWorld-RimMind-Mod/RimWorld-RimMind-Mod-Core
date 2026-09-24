using System.Collections.Generic;
using RimMind.Application.Common.Models.Memory;

namespace RimMind.Application.Common.Interfaces.Memory
{
    /// <summary>
    /// Optional cross-mod memory capability. Core owns the contract so consumers do not
    /// need a compile-time dependency on RimMind-Memory.
    /// </summary>
    public interface IMemoryBridge
    {
        bool AddPawnMemory(string content, MemoryKind kind, int tick, float importance, string? pawnId);
        bool AddNarratorMemory(string content, int tick, float importance);
        IReadOnlyList<NarratorMemoryEntry> GetRecentNarrations(int maxEntries);
    }
}
