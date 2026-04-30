using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public class PriorityFilter : IPerceptionFilter
    {
        private const float DefaultMinImportance = 0.2f;
        private readonly float _minImportance;

        public PriorityFilter(float minImportance = DefaultMinImportance)
        {
            _minImportance = minImportance;
        }

        public List<PerceptionBufferEntry> Filter(List<PerceptionBufferEntry> entries)
        {
            var result = new List<PerceptionBufferEntry>();
            foreach (var entry in entries)
            {
                if (entry.Importance >= _minImportance)
                    result.Add(entry);
            }
            return result;
        }
    }
}
