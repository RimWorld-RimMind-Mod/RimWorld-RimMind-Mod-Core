using System.Collections.Generic;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IPerceptionBuffer
    {
        int Count { get; }
        IReadOnlyList<PerceptionBufferEntry> Entries { get; }
        void Add(PerceptionBufferEntry entry);
        List<PerceptionBufferEntry> Flush();
        void Clear();
    }
}
