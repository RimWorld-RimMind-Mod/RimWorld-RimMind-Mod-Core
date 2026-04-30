using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public class PerceptionPipeline
    {
        private readonly List<IPerceptionFilter> _filters = new List<IPerceptionFilter>();

        public void AddFilter(IPerceptionFilter filter)
        {
            _filters.Add(filter);
        }

        public List<PerceptionBufferEntry> Process(List<PerceptionBufferEntry> entries)
        {
            var current = entries;
            foreach (var filter in _filters)
                current = filter.Filter(current);
            return current;
        }
    }
}
