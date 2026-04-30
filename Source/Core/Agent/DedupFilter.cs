using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Agent
{
    public class DedupFilter : IPerceptionFilter
    {
        private const int DefaultCooldownTicks = 600;
        private const int CleanupIntervalTicks = 60000;
        private readonly Dictionary<string, int> _lastSeen = new Dictionary<string, int>();
        private readonly int _cooldownTicks;
        private int _lastCleanupTick;

        public DedupFilter(int cooldownTicks = DefaultCooldownTicks)
        {
            _cooldownTicks = cooldownTicks;
            _lastCleanupTick = -CleanupIntervalTicks;
        }

        public List<PerceptionBufferEntry> Filter(List<PerceptionBufferEntry> entries)
        {
            var result = new List<PerceptionBufferEntry>();
            foreach (var entry in entries)
            {
                if (_lastSeen.TryGetValue(entry.DedupKey, out int lastTick))
                {
                    if (entry.Timestamp - lastTick < _cooldownTicks)
                        continue;
                }
                _lastSeen[entry.DedupKey] = entry.Timestamp;
                result.Add(entry);
            }

            TryCleanup(entries.Count > 0 ? entries[0].Timestamp : 0);
            return result;
        }

        private void TryCleanup(int currentTick)
        {
            if (currentTick - _lastCleanupTick < CleanupIntervalTicks) return;
            _lastCleanupTick = currentTick;

            var expiredKeys = new List<string>();
            foreach (var kv in _lastSeen)
            {
                if (currentTick - kv.Value > _cooldownTicks * 10)
                    expiredKeys.Add(kv.Key);
            }
            foreach (var key in expiredKeys)
                _lastSeen.Remove(key);
        }

        public void Reset() => _lastSeen.Clear();
    }
}
