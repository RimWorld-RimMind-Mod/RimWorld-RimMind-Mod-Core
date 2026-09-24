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
        private readonly ILogSink? _log;

        public CooldownTable(ILogSink? log = null) { _log = log; }

        public void Set(string modId, int ticksRemaining)
        {
            _table[modId] = ticksRemaining;
        }

        public int Get(string modId)
        {
            return _table.TryGetValue(modId, out var ticks) ? ticks : 0;
        }

        public void Tick()
        {
            foreach (var key in _table.Keys)
            {
                _table.AddOrUpdate(key, 0, (_, v) => Math.Max(0, v - 1));
                if (_table.TryGetValue(key, out var v) && v <= 0)
                    _table.TryRemove(key, out _);
            }
        }

        public void Clear(string modId) => _table.TryRemove(modId, out _);
        public void ClearAll() => _table.Clear();

        public IReadOnlyDictionary<string, int> GetSnapshot()
        {
            return new Dictionary<string, int>(_table);
        }

        public int GetModCooldownTicks(string modId)
        {
            return Get(modId);
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

        public IReadOnlyDictionary<string, int> GetAll()
        {
            return new Dictionary<string, int>(_table);
        }
    }
}
