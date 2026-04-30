using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public interface IPerceptionFilter
    {
        List<PerceptionBufferEntry> Filter(List<PerceptionBufferEntry> entries);
    }
}
