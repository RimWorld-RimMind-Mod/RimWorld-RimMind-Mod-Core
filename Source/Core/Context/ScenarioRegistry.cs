using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Context
{
    public enum L4Mode
    {
        BudgetControlled,
        MemoryOnly,
        NarrativeMemory,
        None
    }

    public class ScenarioMeta
    {
        public string Id = null!;
        public int DefaultBaseRounds;
        public string Description = null!;
        public float[]? DefaultEmbedding;
        public float DefaultBudget = 0.6f;
        public L4Mode L4Mode = L4Mode.BudgetControlled;
        public string[] DefaultExcludeKeys = new string[0];
    }

    public static class ScenarioRegistry
    {
        private static readonly Dictionary<string, ScenarioMeta> _scenarios = new Dictionary<string, ScenarioMeta>();
        private static bool _coreRegistered = false;

        public static void Register(string scenarioId, int defaultBaseRounds, string description,
            float[]? defaultEmbedding = null, float defaultBudget = 0.6f,
            L4Mode l4Mode = L4Mode.BudgetControlled, string[]? defaultExcludeKeys = null)
        {
            if (_scenarios.ContainsKey(scenarioId))
            {
                Log.Warning($"[RimMind] Scenario '{scenarioId}' already registered, overwriting.");
            }
            _scenarios[scenarioId] = new ScenarioMeta
            {
                Id = scenarioId,
                DefaultBaseRounds = defaultBaseRounds,
                Description = description,
                DefaultEmbedding = defaultEmbedding,
                DefaultBudget = defaultBudget,
                L4Mode = l4Mode,
                DefaultExcludeKeys = defaultExcludeKeys ?? new string[0]
            };
        }

        public static bool Unregister(string scenarioId)
        {
            return _scenarios.Remove(scenarioId);
        }

        public static ScenarioMeta? Get(string scenarioId)
        {
            return _scenarios.TryGetValue(scenarioId, out var meta) ? meta : null;
        }

        public static List<ScenarioMeta> GetAll()
        {
            return new List<ScenarioMeta>(_scenarios.Values);
        }

        public static void RegisterCoreScenarios()
        {
            if (_coreRegistered) return;
            _coreRegistered = true;
            Register(ScenarioIds.Dialogue, 10, "对话场景",
                defaultBudget: 0.6f, l4Mode: L4Mode.BudgetControlled,
                defaultExcludeKeys: new[] { "combat_status", "task_progress" });
            Register(ScenarioIds.Decision, 0, "决策场景",
                defaultBudget: 0.5f, l4Mode: L4Mode.None,
                defaultExcludeKeys: new string[0]);
            Register(ScenarioIds.Personality, 3, "人格评估场景",
                defaultBudget: 0.4f, l4Mode: L4Mode.MemoryOnly,
                defaultExcludeKeys: new[] { "combat_status" });
            Register(ScenarioIds.Storyteller, 8, "叙事场景",
                defaultBudget: 0.7f, l4Mode: L4Mode.NarrativeMemory,
                defaultExcludeKeys: new string[0]);
        }

        public static int GetBaseRounds(string scenarioId)
        {
            var meta = Get(scenarioId);
            return meta?.DefaultBaseRounds ?? 6;
        }
    }

    public static class ScenarioIds
    {
        public const string Dialogue = "Dialogue";
        public const string Decision = "Decision";
        public const string Personality = "Personality";
        public const string Storyteller = "Storyteller";
    }
}
