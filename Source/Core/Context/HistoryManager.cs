using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMind.Core.Context
{
    public class HistoryManager
    {
        public static HistoryManager Instance { get; private set; } = null!;

        static HistoryManager()
        {
            Instance = new HistoryManager();
        }

        private readonly ConcurrentDictionary<string, List<HistoryEntry>> _histories =
            new ConcurrentDictionary<string, List<HistoryEntry>>();
        private readonly object _listLock = new object();

        public void AddTurn(string npcId, string userMessage, string assistantMessage, string? scenario = null)
        {
            var entries = _histories.GetOrAdd(npcId, _ => new List<HistoryEntry>());
            int tick = Find.TickManager?.TicksGame ?? 0;
            lock (_listLock)
            {
                entries.Add(new HistoryEntry("user", userMessage, tick, scenario));
                entries.Add(new HistoryEntry("assistant", assistantMessage, tick, scenario));
            }
        }

        public List<(string role, string content)> GetHistory(string npcId, int maxRounds)
        {
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0)
                return new List<(string, string)>();

            int maxEntries = maxRounds * 2;
            List<HistoryEntry> snapshot;
            lock (_listLock)
            {
                snapshot = entries.Skip(Math.Max(0, entries.Count - maxEntries)).ToList();
            }
            return snapshot.Select(e => (e.Role, e.Content)).ToList();
        }

        public List<(string role, string content)> GetHistory(string npcId, int maxRounds, string? scenarioFilter)
        {
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0)
                return new List<(string, string)>();

            List<HistoryEntry> snapshot;
            lock (_listLock)
            {
                snapshot = entries.ToList();
            }

            IEnumerable<HistoryEntry> filtered = snapshot;
            if (!string.IsNullOrEmpty(scenarioFilter))
                filtered = filtered.Where(e => e.Scenario == scenarioFilter);

            var list = filtered.ToList();
            int maxEntries = maxRounds * 2;
            var recent = list.Skip(Math.Max(0, list.Count - maxEntries)).ToList();
            return recent.Select(e => (e.Role, e.Content)).ToList();
        }

        public void ClearHistory(string npcId)
        {
            _histories.TryRemove(npcId, out _);
        }

        public int GetHistoryCount(string npcId)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return 0;
            lock (_listLock) { return entries.Count; }
        }

        public int GetMaxRounds(string scenarioId, float budget)
        {
            int baseRounds = ScenarioRegistry.GetBaseRounds(scenarioId);
            if (baseRounds == 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(budget * baseRounds));
        }

        public void CompressIfNeeded(string npcId, int maxEntries = 40)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return;
            lock (_listLock)
            {
                if (entries.Count <= maxEntries) return;
                int removeCount = entries.Count - maxEntries;
                entries.RemoveRange(0, removeCount);
            }
        }

        public Dictionary<string, List<HistoryEntry>> GetAllForSave()
        {
            var result = new Dictionary<string, List<HistoryEntry>>();
            foreach (var kvp in _histories)
            {
                lock (_listLock)
                {
                    result[kvp.Key] = new List<HistoryEntry>(kvp.Value);
                }
            }
            return result;
        }

        public void LoadFromSave(Dictionary<string, List<HistoryEntry>> data)
        {
            _histories.Clear();
            foreach (var kvp in data)
                _histories[kvp.Key] = new List<HistoryEntry>(kvp.Value);
        }

        public void ReplaceLastAssistantTurn(string npcId, string newContent)
        {
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0) return;

            lock (_listLock)
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Role == "assistant")
                    {
                        entries[i] = new HistoryEntry("assistant", newContent, entries[i].Tick, entries[i].Scenario);
                        return;
                    }
                }
            }
        }
    }
}
