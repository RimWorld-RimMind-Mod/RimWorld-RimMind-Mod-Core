using System;
using System.Collections.Generic;
using System.Threading;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Agent
{
    public sealed class AgentLoopScheduler : IAgentLoopScheduler
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private Entry[] _cachedEntries = Array.Empty<Entry>();
        private bool _entriesDirty = true;
        private readonly ILogSink? _logSink;
        private long _generation;
        private int _lastTick = -1;
        private int _tickedAgents;
        private int _faultedAgents;
        private bool _isLoopActive;
        private int _activeTick = -1;
        private long _activeGeneration = -1;
        private PendingTick? _pendingTick;

        public AgentLoopScheduler(ILogSink? logSink = null)
        {
            _logSink = logSink;
        }

        public long Generation => Interlocked.Read(ref _generation);

        public bool Register(string key, AgentLoopKind kind, IAgentControl agent)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Agent loop key cannot be blank.", nameof(key));
            if (agent == null)
                throw new ArgumentNullException(nameof(agent));
            if (kind != AgentLoopKind.Pawn && kind != AgentLoopKind.Scoped)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported agent loop kind.");

            lock (_syncRoot)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && existing.Kind == kind
                    && ReferenceEquals(existing.Agent, agent))
                {
                    return false;
                }

                _entries[key] = new Entry(key, kind, agent);
                _entriesDirty = true;
                return true;
            }
        }

        public bool Unregister(string key)
        {
            lock (_syncRoot)
            {
                if (_entries.Remove(key))
                {
                    _entriesDirty = true;
                    return true;
                }
                return false;
            }
        }

        public IAgentControl? Find(string key)
        {
            lock (_syncRoot)
            {
                return _entries.TryGetValue(key, out var entry) ? entry.Agent : null;
            }
        }

        public void Tick(int currentTick)
        {
            long loopGeneration;
            lock (_syncRoot)
            {
                long currentGeneration = _generation;
                if (_isLoopActive)
                {
                    if (currentGeneration == _activeGeneration && currentTick <= _activeTick)
                        return;

                    if (!_pendingTick.HasValue
                        || currentGeneration > _pendingTick.Value.Generation
                        || (currentGeneration == _pendingTick.Value.Generation
                            && currentTick > _pendingTick.Value.Tick))
                    {
                        _pendingTick = new PendingTick(currentTick, currentGeneration);
                    }
                    return;
                }

                if (_pendingTick.HasValue)
                {
                    PendingTick pending = _pendingTick.Value;
                    _pendingTick = null;
                    if (pending.Generation == currentGeneration && pending.Tick > currentTick)
                        currentTick = pending.Tick;
                }

                if (currentTick <= _lastTick)
                    return;

                _isLoopActive = true;
                _activeTick = currentTick;
                _activeGeneration = currentGeneration;
                loopGeneration = currentGeneration;
            }

            var tickToRun = currentTick;
            try
            {
                while (true)
                {
                    Entry[] tickEntries;
                    lock (_syncRoot)
                    {
                        if (_entriesDirty)
                        {
                            _cachedEntries = new Entry[_entries.Count];
                            _entries.Values.CopyTo(_cachedEntries, 0);
                            _entriesDirty = false;
                        }
                        tickEntries = _cachedEntries;
                    }

                    var tickedAgents = 0;
                    var faultedAgents = 0;
                    foreach (var entry in tickEntries)
                    {
                        if (Generation != loopGeneration)
                            break;

                        try
                        {
                            entry.Agent.Tick();
                            tickedAgents++;
                        }
                        catch (Exception ex)
                        {
                            faultedAgents++;
                            _logSink?.Error(
                                $"[RimMind.AgentLoop] action=TickFailed key={entry.Key} kind={entry.Kind} error={ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    lock (_syncRoot)
                    {
                        if (Generation != loopGeneration)
                        {
                            if (_pendingTick.HasValue
                                && _pendingTick.Value.Generation == _generation)
                            {
                                PendingTick pending = _pendingTick.Value;
                                _pendingTick = null;
                                tickToRun = pending.Tick;
                                loopGeneration = pending.Generation;
                                _activeTick = tickToRun;
                                _activeGeneration = loopGeneration;
                                continue;
                            }

                            _isLoopActive = false;
                            _activeTick = -1;
                            _activeGeneration = -1;
                            _pendingTick = null;
                            return;
                        }

                        _lastTick = tickToRun;
                        _tickedAgents = tickedAgents;
                        _faultedAgents = faultedAgents;

                        if (_pendingTick.HasValue)
                        {
                            PendingTick pending = _pendingTick.Value;
                            tickToRun = pending.Tick;
                            loopGeneration = pending.Generation;
                            _pendingTick = null;
                            _activeTick = tickToRun;
                            _activeGeneration = loopGeneration;
                            continue;
                        }

                        _isLoopActive = false;
                        _activeTick = -1;
                        _activeGeneration = -1;
                        return;
                    }
                }
            }
            catch
            {
                lock (_syncRoot)
                {
                    if (_isLoopActive
                        && _activeGeneration == loopGeneration
                        && _activeTick == tickToRun)
                    {
                        _isLoopActive = false;
                        _activeTick = -1;
                        _activeGeneration = -1;
                        if (_pendingTick.HasValue
                            && _pendingTick.Value.Generation == loopGeneration)
                        {
                            _pendingTick = null;
                        }
                    }
                }

                throw;
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _entries.Clear();
                _entriesDirty = true;
                _cachedEntries = Array.Empty<Entry>();
                Interlocked.Increment(ref _generation);
                _lastTick = -1;
                _tickedAgents = 0;
                _faultedAgents = 0;
                _pendingTick = null;
            }
        }

        public AgentLoopSnapshot GetSnapshot()
        {
            List<Entry> entries;
            int lastTick;
            int tickedAgents;
            int faultedAgents;
            lock (_syncRoot)
            {
                entries = new List<Entry>(_entries.Values);
                lastTick = _lastTick;
                tickedAgents = _tickedAgents;
                faultedAgents = _faultedAgents;
            }

            var registeredPawnAgents = 0;
            var registeredScopedAgents = 0;
            var activeAgents = 0;
            var pausedAgents = 0;
            var dormantAgents = 0;
            var terminatedAgents = 0;

            foreach (var entry in entries)
            {
                if (entry.Kind == AgentLoopKind.Pawn)
                    registeredPawnAgents++;
                else if (entry.Kind == AgentLoopKind.Scoped)
                    registeredScopedAgents++;

                switch (entry.Agent.State)
                {
                    case AgentState.Active:
                        activeAgents++;
                        break;
                    case AgentState.Paused:
                        pausedAgents++;
                        break;
                    case AgentState.Terminated:
                        terminatedAgents++;
                        break;
                    case AgentState.Dormant:
                        dormantAgents++;
                        break;
                }
            }

            return new AgentLoopSnapshot(
                registeredPawnAgents,
                registeredScopedAgents,
                activeAgents,
                pausedAgents,
                dormantAgents,
                terminatedAgents,
                lastTick,
                tickedAgents,
                faultedAgents);
        }

        private sealed class Entry
        {
            public Entry(string key, AgentLoopKind kind, IAgentControl agent)
            {
                Key = key;
                Kind = kind;
                Agent = agent;
            }

            public string Key { get; }
            public AgentLoopKind Kind { get; }
            public IAgentControl Agent { get; }
        }

        private readonly struct PendingTick
        {
            public PendingTick(int tick, long generation)
            {
                Tick = tick;
                Generation = generation;
            }

            public int Tick { get; }
            public long Generation { get; }
        }
    }
}
