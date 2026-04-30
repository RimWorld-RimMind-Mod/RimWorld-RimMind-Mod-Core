using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public class CooldownFilter : IPerceptionFilter
    {
        private const int DefaultTypeCooldownTicks = 3000;
        private readonly Dictionary<string, int> _lastTypeSeen = new Dictionary<string, int>();
        private readonly int _cooldownTicks;

        public CooldownFilter(int cooldownTicks = DefaultTypeCooldownTicks)
        {
            _cooldownTicks = cooldownTicks;
        }

        public List<PerceptionBufferEntry> Filter(List<PerceptionBufferEntry> entries)
        {
            var result = new List<PerceptionBufferEntry>();
            int now = entries.Count > 0 ? entries[0].Timestamp : 0;

            foreach (var entry in entries)
            {
                if (_lastTypeSeen.TryGetValue(entry.PerceptionType, out int lastTick))
                {
                    if (entry.Timestamp - lastTick < _cooldownTicks)
                        continue;
                }
                _lastTypeSeen[entry.PerceptionType] = entry.Timestamp;
                result.Add(entry);
            }

            TryCleanup(now);
            return result;
        }

        private void TryCleanup(int currentTick)
        {
            if (currentTick <= 0) return;
            var expired = new List<string>();
            foreach (var kv in _lastTypeSeen)
            {
                if (currentTick - kv.Value > _cooldownTicks * 5)
                    expired.Add(kv.Key);
            }
            foreach (var key in expired)
                _lastTypeSeen.Remove(key);
        }

        public void Reset() => _lastTypeSeen.Clear();
    }
}
