using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Context
{
    public static class RelevanceTable
    {
        private static readonly Dictionary<(string, string), float> _table = new Dictionary<(string, string), float>();
        private static bool _coreRegistered = false;
        private const float DefaultRelevance = 0.5f;

        public static void Register(string scenarioId, string key, float relevance)
        {
            _table[(scenarioId, key)] = relevance;
        }

        public static void RegisterBatch(string scenarioId, Dictionary<string, float> entries)
        {
            foreach (var kvp in entries)
                _table[(scenarioId, kvp.Key)] = kvp.Value;
        }

        public static bool Unregister(string scenarioId, string key)
        {
            return _table.Remove((scenarioId, key));
        }

        public static bool UnregisterScenario(string scenarioId)
        {
            bool removed = false;
            var keysToRemove = new List<(string, string)>();
            foreach (var kvp in _table)
            {
                if (kvp.Key.Item1 == scenarioId)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var k in keysToRemove)
            {
                _table.Remove(k);
                removed = true;
            }
            return removed;
        }

        public static float GetRelevance(string scenarioId, string key)
        {
            return _table.TryGetValue((scenarioId, key), out var value) ? value : DefaultRelevance;
        }

        public static void RegisterCoreRelevance()
        {
            if (_coreRegistered) return;
            _coreRegistered = true;

            RegisterBatch(ScenarioIds.Decision, new Dictionary<string, float>
            {
                {"health", 0.9f}, {"mood", 0.8f}, {"current_job", 0.9f},
                {"combat_status", 0.95f}, {"target_info", 0.9f}, {"task_progress", 0.85f},
                {"nearby_pawns", 0.7f}, {"colony_status", 0.6f}, {"current_area", 0.5f},
                {"weather", 0.2f}, {"time_of_day", 0.3f}, {"season", 0.1f},
                {"map_structure", 0.3f}, {"pawn_base_info", 0.4f}, {"fixed_relations", 0.5f},
                {"ideology", 0.3f}, {"skills_summary", 0.7f}, {"memory_pawn", 0.6f},
                {"working_memory", 0.8f}, {"memory_narrator", 0.2f}
            });

            RegisterBatch(ScenarioIds.Dialogue, new Dictionary<string, float>
            {
                {"health", 0.6f}, {"mood", 0.9f}, {"current_job", 0.5f},
                {"combat_status", 0.4f}, {"target_info", 0.3f}, {"task_progress", 0.3f},
                {"nearby_pawns", 0.8f}, {"colony_status", 0.5f}, {"current_area", 0.6f},
                {"weather", 0.3f}, {"time_of_day", 0.4f}, {"season", 0.2f},
                {"map_structure", 0.2f}, {"pawn_base_info", 0.7f}, {"fixed_relations", 0.85f},
                {"ideology", 0.6f}, {"skills_summary", 0.4f}, {"memory_pawn", 0.7f},
                {"working_memory", 0.6f}, {"memory_narrator", 0.3f}
            });

            RegisterBatch(ScenarioIds.Personality, new Dictionary<string, float>
            {
                {"health", 0.5f}, {"mood", 0.85f}, {"current_job", 0.6f},
                {"combat_status", 0.3f}, {"target_info", 0.2f}, {"task_progress", 0.4f},
                {"nearby_pawns", 0.6f}, {"colony_status", 0.4f}, {"current_area", 0.3f},
                {"weather", 0.1f}, {"time_of_day", 0.2f}, {"season", 0.1f},
                {"map_structure", 0.2f}, {"pawn_base_info", 0.9f}, {"fixed_relations", 0.8f},
                {"ideology", 0.85f}, {"skills_summary", 0.7f}, {"memory_pawn", 0.8f},
                {"working_memory", 0.5f}, {"memory_narrator", 0.3f}
            });

            RegisterBatch(ScenarioIds.Storyteller, new Dictionary<string, float>
            {
                {"health", 0.3f}, {"mood", 0.4f}, {"current_job", 0.2f},
                {"combat_status", 0.8f}, {"target_info", 0.7f}, {"task_progress", 0.3f},
                {"nearby_pawns", 0.5f}, {"colony_status", 0.9f}, {"current_area", 0.4f},
                {"weather", 0.6f}, {"time_of_day", 0.5f}, {"season", 0.7f},
                {"map_structure", 0.8f}, {"pawn_base_info", 0.3f}, {"fixed_relations", 0.3f},
                {"ideology", 0.4f}, {"skills_summary", 0.2f}, {"memory_pawn", 0.3f},
                {"working_memory", 0.2f}, {"memory_narrator", 0.9f}
            });
        }

        public static void Clear()
        {
            _table.Clear();
            _coreRegistered = false;
        }
    }
}
