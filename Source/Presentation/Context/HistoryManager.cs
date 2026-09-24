using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Models;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Context
{
    public class HistoryManager : IHistoryManager
    {
        private readonly ConcurrentDictionary<string, List<HistoryEntry>> _histories =
            new ConcurrentDictionary<string, List<HistoryEntry>>();
        private readonly object _listLock = new object();
        private readonly ITickProvider? _tickProvider;

        private const int MaxEntriesPerNpc = RimMindDefaults.MaxEntriesPerNpc;
        private const int CompressThreshold = RimMindDefaults.HistoryCompressThreshold;

        public HistoryManager(ITickProvider? tickProvider = null)
        {
            _tickProvider = tickProvider;
        }

        private int CurrentTick => _tickProvider?.TicksGame ?? 0;

        public void AddTurn(string npcId, string userMessage, string assistantMessage, string? scenario = null)
        {
            var entries = _histories.GetOrAdd(npcId, _ => new List<HistoryEntry>());
            int tick = CurrentTick;
            lock (_listLock)
            {
                entries.Add(new HistoryEntry("user", userMessage, tick, scenario));
                entries.Add(new HistoryEntry("assistant", assistantMessage, tick, scenario));
                EnforceCapacity(entries);
            }
        }

        public void AddPendingTurn(
            string npcId,
            string turnId,
            string userMessage,
            string assistantPlaceholder,
            string? scenario = null)
        {
            var entries = _histories.GetOrAdd(npcId, _ => new List<HistoryEntry>());
            int tick = CurrentTick;
            lock (_listLock)
            {
                entries.Add(new HistoryEntry("user", userMessage, tick, scenario, turnId, isPending: true));
                entries.Add(new HistoryEntry("assistant", assistantPlaceholder, tick, scenario, turnId, isPending: true));
            }
        }

        public List<(string role, string content)> GetHistory(string npcId, int maxRounds, string? scenario = null)
            => GetHistorySnapshot(npcId, maxRounds, scenario, includePending: false);

        public List<(string role, string content)> GetHistoryForDisplay(
            string npcId,
            int maxRounds,
            string? scenario = null)
            => GetHistorySnapshot(npcId, maxRounds, scenario, includePending: true);

        private List<(string role, string content)> GetHistorySnapshot(
            string npcId,
            int maxRounds,
            string? scenario,
            bool includePending)
        {
            if (maxRounds <= 0)
                return new List<(string, string)>();
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0)
                return new List<(string, string)>();
            List<HistoryEntry> snapshot;
            lock (_listLock) { snapshot = entries.ToList(); }

            var candidates = snapshot
                .Where(entry => (includePending || !entry.IsPending)
                    && (scenario == null
                        || string.Equals(entry.Scenario, scenario, StringComparison.Ordinal)))
                .ToList();
            var rounds = new List<(HistoryEntry user, HistoryEntry assistant)>();
            for (int index = 0; index + 1 < candidates.Count;)
            {
                HistoryEntry user = candidates[index];
                HistoryEntry assistant = candidates[index + 1];
                if (user.Role == "user" && assistant.Role == "assistant")
                {
                    rounds.Add((user, assistant));
                    index += 2;
                }
                else
                {
                    index++;
                }
            }

            int firstRound = Math.Max(0, rounds.Count - maxRounds);
            var result = new List<(string, string)>((rounds.Count - firstRound) * 2);
            for (int index = firstRound; index < rounds.Count; index++)
            {
                result.Add((rounds[index].user.Role, rounds[index].user.Content));
                result.Add((rounds[index].assistant.Role, rounds[index].assistant.Content));
            }
            return result;
        }

        public int GetHistoryCount(string npcId)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return 0;
            lock (_listLock) { return entries.Count; }
        }

        public void ClearHistory(string npcId)
        {
            _histories.TryRemove(npcId, out _);
        }

        public void CompressIfNeeded(string npcId)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return;
            lock (_listLock)
            {
                EnforceCapacity(entries);
            }
        }

        private static void EnforceCapacity(List<HistoryEntry> entries)
        {
            if (entries.Count <= MaxEntriesPerNpc)
                return;

            var kept = entries.Skip(entries.Count - CompressThreshold).ToList();
            entries.Clear();
            entries.AddRange(kept);
        }

        public void ReplaceLastAssistantTurn(string npcId, string content)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return;
            lock (_listLock)
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Role == "assistant")
                    {
                        entries[i] = new HistoryEntry("assistant", content, entries[i].Tick, entries[i].Scenario);
                        break;
                    }
                }
            }
        }

        public bool ReplaceAssistantTurn(string npcId, string turnId, string content)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return false;
            lock (_listLock)
            {
                int assistantIndex = entries.FindLastIndex(entry =>
                    entry.Role == "assistant"
                    && entry.IsPending
                    && string.Equals(entry.TurnId, turnId, StringComparison.Ordinal));
                if (assistantIndex < 0)
                    return false;

                HistoryEntry assistant = entries[assistantIndex];
                foreach (HistoryEntry entry in entries)
                {
                    if (entry.IsPending
                        && string.Equals(entry.TurnId, turnId, StringComparison.Ordinal))
                    {
                        entry.IsPending = false;
                        entry.TurnId = null;
                    }
                }
                entries[assistantIndex] = new HistoryEntry(
                    "assistant",
                    content,
                    assistant.Tick,
                    assistant.Scenario,
                    turnId: null,
                    isPending: false);
                EnforceCapacity(entries);
                return true;
            }
        }

        public bool RemoveTurn(string npcId, string turnId)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return false;
            lock (_listLock)
            {
                return entries.RemoveAll(entry =>
                    entry.IsPending
                    &&
                    string.Equals(entry.TurnId, turnId, StringComparison.Ordinal)) > 0;
            }
        }

        public string GetAllForSave()
        {
            var dict = GetAllForSaveDict();
            return Newtonsoft.Json.JsonConvert.SerializeObject(dict);
        }

        public Dictionary<string, List<HistoryEntry>> GetAllForSaveDict()
        {
            var result = new Dictionary<string, List<HistoryEntry>>();
            foreach (var kvp in _histories)
            {
                lock (_listLock)
                {
                    result[kvp.Key] = kvp.Value
                        .Where(entry => !entry.IsPending)
                        .ToList();
                }
            }
            return result;
        }

        public void LoadFromSave(Dictionary<string, List<HistoryEntry>> data)
        {
            lock (_listLock)
            {
                _histories.Clear();
                if (data == null) return;
                foreach (var kvp in data)
                {
                    _histories[kvp.Key] = (kvp.Value ?? new List<HistoryEntry>())
                        .Where(entry => entry != null && !entry.IsPending)
                        .ToList();
                }
            }
        }
    }
}
