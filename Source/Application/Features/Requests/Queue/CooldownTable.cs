using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Application.Features.Requests.Queue
{
    public sealed class CooldownTable
    {
        private readonly ConcurrentDictionary<string, int> _table
            = new ConcurrentDictionary<string, int>();

        public CooldownTable(ILogSink? log = null) { }

        public void Set(string modId, int nextAllowedTick)
        {
            _table[modId] = nextAllowedTick;
        }

        public int Get(string modId)
        {
            return _table.TryGetValue(modId, out var ticks) ? ticks : 0;
        }

        public void Clear(string modId) => _table.TryRemove(modId, out _);
        public void ClearAll() => _table.Clear();

        public IReadOnlyDictionary<string, int> GetSnapshot()
        {
            return new Dictionary<string, int>(_table);
        }

        public bool IsOnCooldown(string modId, int currentTick)
        {
            if (!_table.TryGetValue(modId, out var nextAllowed)) return false;
            return currentTick < nextAllowed;
        }

        public int GetCooldownTicksLeft(string modId, int currentTick)
        {
            if (!_table.TryGetValue(modId, out var nextAllowed)) return 0;
            return Math.Max(0, nextAllowed - currentTick);
        }

        public IReadOnlyDictionary<string, int> GetAll() => GetSnapshot();
    }
}
