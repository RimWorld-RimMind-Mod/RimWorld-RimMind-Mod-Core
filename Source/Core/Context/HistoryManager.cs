using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMind.Core.Context
{
    public class HistoryManager
    {
        private static HistoryManager? _instance;
        public static HistoryManager Instance => _instance ??= new HistoryManager();

        private readonly Dictionary<string, List<HistoryEntry>> _histories = new Dictionary<string, List<HistoryEntry>>();

        /// <summary>
        /// 添加一轮对话记录
        /// </summary>
        public void AddTurn(string npcId, string userMessage, string assistantMessage, string? scenario = null)
        {
            if (!_histories.ContainsKey(npcId))
                _histories[npcId] = new List<HistoryEntry>();

            int tick = Find.TickManager?.TicksGame ?? 0;
            _histories[npcId].Add(new HistoryEntry("user", userMessage, tick, scenario));
            _histories[npcId].Add(new HistoryEntry("assistant", assistantMessage, tick, scenario));
        }

        /// <summary>
        /// 获取指定NPC的历史记录，maxRounds为对话轮数（每轮包含user+assistant两条）
        /// </summary>
        public List<(string role, string content)> GetHistory(string npcId, int maxRounds)
        {
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0)
                return new List<(string, string)>();

            int maxEntries = maxRounds * 2;
            var recent = entries.Skip(Math.Max(0, entries.Count - maxEntries)).ToList();
            return recent.Select(e => (e.Role, e.Content)).ToList();
        }

        /// <summary>
        /// 清除指定NPC的历史记录
        /// </summary>
        public void ClearHistory(string npcId)
        {
            _histories.Remove(npcId);
        }

        /// <summary>
        /// 获取指定NPC的历史条目数
        /// </summary>
        public int GetHistoryCount(string npcId)
        {
            return _histories.TryGetValue(npcId, out var entries) ? entries.Count : 0;
        }

        /// <summary>
        /// 根据场景和预算系数计算最大对话轮数
        /// </summary>
        public int GetMaxRounds(string scenarioId, float budget)
        {
            int baseRounds = ScenarioRegistry.GetBaseRounds(scenarioId);
            if (baseRounds == 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(budget * baseRounds));
        }

        /// <summary>
        /// 当历史条目超过上限时，移除最早的记录
        /// </summary>
        public void CompressIfNeeded(string npcId, int maxEntries = 40)
        {
            if (!_histories.TryGetValue(npcId, out var entries)) return;
            if (entries.Count <= maxEntries) return;

            int removeCount = entries.Count - maxEntries;
            entries.RemoveRange(0, removeCount);
        }

        /// <summary>
        /// 导出全部历史数据用于存档
        /// </summary>
        public Dictionary<string, List<HistoryEntry>> GetAllForSave()
        {
            return new Dictionary<string, List<HistoryEntry>>(_histories);
        }

        /// <summary>
        /// 从存档数据恢复历史记录
        /// </summary>
        public void LoadFromSave(Dictionary<string, List<HistoryEntry>> data)
        {
            _histories.Clear();
            foreach (var kvp in data)
                _histories[kvp.Key] = new List<HistoryEntry>(kvp.Value);
        }

        public void ReplaceLastAssistantTurn(string npcId, string newContent)
        {
            if (!_histories.TryGetValue(npcId, out var entries) || entries.Count == 0) return;

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
