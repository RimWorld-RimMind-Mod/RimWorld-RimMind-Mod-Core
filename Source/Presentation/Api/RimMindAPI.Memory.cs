using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Memory;
using RimMind.Application.Common.Models.Memory;
using RimMind.Application.Features.Memory;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>Optional typed integration point implemented by RimMind-Memory.</summary>
        public static class Memory
        {
            public static void RegisterBridge(IMemoryBridge bridge)
                => MemoryBridgeRegistry.Register(bridge);

            public static bool AddPawnMemory(string content, MemoryKind kind, int tick, float importance, string? pawnId = null)
                => MemoryBridgeRegistry.Current.AddPawnMemory(content, kind, tick, importance, pawnId);

            public static bool AddNarratorMemory(string content, int tick, float importance)
                => MemoryBridgeRegistry.Current.AddNarratorMemory(content, tick, importance);

            public static IReadOnlyList<NarratorMemoryEntry> GetRecentNarrations(int maxEntries)
                => MemoryBridgeRegistry.Current.GetRecentNarrations(maxEntries);
        }
    }
}
