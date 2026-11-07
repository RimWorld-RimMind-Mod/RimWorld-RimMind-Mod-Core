using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;

namespace RimMind.Application.Features.Context
{
    public enum L4Mode
    {
        BudgetControlled,
        MemoryOnly,
        NarrativeMemory,
        None
    }

    public sealed class ScenarioDescriptor
    {
        public string Id { get; init; } = null!;
        public int DefaultBaseRounds { get; init; } = 6;
        public string Description { get; init; } = string.Empty;
        public float[]? DefaultEmbedding { get; init; }
        public float DefaultBudget { get; init; } = 0.6f;
        public L4Mode L4Mode { get; init; } = L4Mode.BudgetControlled;
        public string[] DefaultExcludeKeys { get; init; } = new string[0];
        public IReadOnlyDictionary<string, float>? RelevanceWeights { get; init; }
        public string? OwnerModId { get; init; }
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
        public IReadOnlyDictionary<string, float>? RelevanceWeights;
        public string? OwnerModId;
        internal bool IsCoreFallback;
    }

    public static class ScenarioRegistry
    {
        private static readonly ConcurrentDictionary<string, ScenarioMeta> _scenarios =
            new ConcurrentDictionary<string, ScenarioMeta>();
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, float>> _relevanceWeights =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, float>>();
        private static bool _coreRegistered = false;

        public static void Register(string scenarioId, int defaultBaseRounds, string description,
            float[]? defaultEmbedding = null, float defaultBudget = 0.6f,
            L4Mode l4Mode = L4Mode.BudgetControlled, string[]? defaultExcludeKeys = null,
            ILogSink? logSink = null)
        {
            Register(
                scenarioId,
                defaultBaseRounds,
                description,
                relevanceWeights: null,
                defaultEmbedding: defaultEmbedding,
                defaultBudget: defaultBudget,
                l4Mode: l4Mode,
                defaultExcludeKeys: defaultExcludeKeys,
                logSink: logSink,
                ownerModId: null);
        }

        public static void Register(string scenarioId, int defaultBaseRounds, string description,
            IReadOnlyDictionary<string, float>? relevanceWeights,
            float[]? defaultEmbedding = null, float defaultBudget = 0.6f,
            L4Mode l4Mode = L4Mode.BudgetControlled, string[]? defaultExcludeKeys = null,
            ILogSink? logSink = null, string? ownerModId = null)
        {
            RegisterInternal(
                new ScenarioDescriptor
                {
                    Id = scenarioId,
                    DefaultBaseRounds = defaultBaseRounds,
                    Description = description,
                    DefaultEmbedding = defaultEmbedding,
                    DefaultBudget = defaultBudget,
                    L4Mode = l4Mode,
                    DefaultExcludeKeys = defaultExcludeKeys ?? new string[0],
                    RelevanceWeights = relevanceWeights,
                    OwnerModId = ownerModId
                },
                logSink,
                isCoreFallback: false);
        }

        public static void Register(ScenarioDescriptor descriptor, ILogSink? logSink = null)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            RegisterInternal(descriptor, logSink, isCoreFallback: false);
        }

        private static void RegisterInternal(ScenarioDescriptor descriptor, ILogSink? logSink, bool isCoreFallback)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException("Scenario id cannot be empty.", nameof(descriptor));

            if (_scenarios.TryGetValue(descriptor.Id, out var existing))
            {
                if (isCoreFallback)
                {
                    if (!existing.IsCoreFallback)
                    {
                        if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(descriptor.Description))
                        {
                            existing.Description = descriptor.Description;
                        }
                        return;
                    }
                }
                else if (!existing.IsCoreFallback)
                {
                    logSink?.Warning($"[RimMind-Core] Scenario '{descriptor.Id}' already registered, overwriting.");
                }
            }

            Dictionary<string, float>? copiedWeights = null;
            if (descriptor.RelevanceWeights != null)
            {
                copiedWeights = CopyWeights(descriptor.RelevanceWeights);
                RegisterRelevance(descriptor.Id, copiedWeights);
            }
            else if (_relevanceWeights.TryGetValue(descriptor.Id, out var storedWeights))
            {
                copiedWeights = new Dictionary<string, float>(storedWeights);
            }

            _scenarios[descriptor.Id] = new ScenarioMeta
            {
                Id = descriptor.Id,
                DefaultBaseRounds = descriptor.DefaultBaseRounds,
                Description = descriptor.Description,
                DefaultEmbedding = descriptor.DefaultEmbedding,
                DefaultBudget = descriptor.DefaultBudget,
                L4Mode = descriptor.L4Mode,
                DefaultExcludeKeys = descriptor.DefaultExcludeKeys ?? new string[0],
                RelevanceWeights = copiedWeights,
                OwnerModId = descriptor.OwnerModId,
                IsCoreFallback = isCoreFallback
            };
        }

        private static Dictionary<string, float> CopyWeights(IReadOnlyDictionary<string, float> source)
        {
            var copy = new Dictionary<string, float>(source.Count);
            foreach (var kvp in source)
            {
                copy[kvp.Key] = kvp.Value;
            }
            return copy;
        }

        public static void RegisterRelevance(string scenarioId, IReadOnlyDictionary<string, float> weights)
        {
            if (string.IsNullOrWhiteSpace(scenarioId) || weights == null) return;
            var map = _relevanceWeights.GetOrAdd(scenarioId, _ => new ConcurrentDictionary<string, float>());
            foreach (var kvp in weights)
            {
                map[kvp.Key] = kvp.Value;
            }

            if (_scenarios.TryGetValue(scenarioId, out var meta))
            {
                meta.RelevanceWeights = new Dictionary<string, float>(map);
            }
        }

        public static bool TryGetRelevance(string scenarioId, string key, out float relevance)
        {
            if (!string.IsNullOrEmpty(scenarioId)
                && !string.IsNullOrEmpty(key)
                && _relevanceWeights.TryGetValue(scenarioId, out var map)
                && map.TryGetValue(key, out relevance))
            {
                return true;
            }

            relevance = 0f;
            return false;
        }

        public static IReadOnlyDictionary<string, float>? GetRelevanceWeights(string scenarioId)
        {
            if (!string.IsNullOrEmpty(scenarioId) && _relevanceWeights.TryGetValue(scenarioId, out var map))
            {
                return new Dictionary<string, float>(map);
            }
            return null;
        }

        public static bool UnregisterRelevance(string scenarioId, string key)
        {
            if (!string.IsNullOrEmpty(scenarioId)
                && !string.IsNullOrEmpty(key)
                && _relevanceWeights.TryGetValue(scenarioId, out var map))
            {
                bool removed = map.TryRemove(key, out _);
                if (removed && _scenarios.TryGetValue(scenarioId, out var meta))
                {
                    meta.RelevanceWeights = new Dictionary<string, float>(map);
                }
                return removed;
            }
            return false;
        }

        public static bool Unregister(string scenarioId)
        {
            _relevanceWeights.TryRemove(scenarioId, out _);
            return _scenarios.TryRemove(scenarioId, out _);
        }

        public static ScenarioMeta? Get(string scenarioId)
        {
            return _scenarios.TryGetValue(scenarioId, out var meta) ? meta : null;
        }

        public static List<ScenarioMeta> GetAll()
        {
            return new List<ScenarioMeta>(_scenarios.Values);
        }

        public static void RegisterCoreScenarios(ITranslationService? translationService = null, ILogSink? logSink = null)
        {
            if (_coreRegistered) return;
            _coreRegistered = true;
            RegisterCoreFallback(ScenarioIds.Dialogue, 10, Translate(translationService, "RimMind.Application.Scenario.Dialogue"),
                defaultBudget: 0.6f, l4Mode: L4Mode.BudgetControlled,
                defaultExcludeKeys: new[] { "combat_status", "task_progress" }, logSink: logSink);
            RegisterCoreFallback(ScenarioIds.CrisisDialogue, 8, Translate(translationService, "RimMind.Application.Scenario.CrisisDialogue"),
                defaultBudget: 0.7f, l4Mode: L4Mode.BudgetControlled,
                defaultExcludeKeys: new[] { "task_progress" }, logSink: logSink);
            RegisterCoreFallback(ScenarioIds.Decision, 0, Translate(translationService, "RimMind.Application.Scenario.Decision"),
                defaultBudget: 0.5f, l4Mode: L4Mode.None,
                defaultExcludeKeys: new string[0], logSink: logSink);
            RegisterCoreFallback(ScenarioIds.Personality, 3, Translate(translationService, "RimMind.Application.Scenario.Personality"),
                defaultBudget: 0.4f, l4Mode: L4Mode.MemoryOnly,
                defaultExcludeKeys: new[] { "combat_status" }, logSink: logSink);
            RegisterCoreFallback(ScenarioIds.Storyteller, 8, Translate(translationService, "RimMind.Application.Scenario.Storyteller"),
                defaultBudget: 0.7f, l4Mode: L4Mode.NarrativeMemory,
                defaultExcludeKeys: new[] { "npc_identity", "pawn_base_info", "fixed_relations",
                    "ideology", "skills_summary", "health", "mood", "current_job",
                    "combat_status", "target_info", "task_progress", "npc_commands" }, logSink: logSink);
            RegisterCoreFallback(ScenarioIds.Memory, 0, Translate(translationService, "RimMind.Application.Scenario.Memory"),
                defaultBudget: 0.4f, l4Mode: L4Mode.None,
                defaultExcludeKeys: new[] { "combat_status", "current_job", "mood",
                    "task_progress", "npc_commands", "target_info" }, logSink: logSink);
        }

        private static void RegisterCoreFallback(string scenarioId, int defaultBaseRounds, string description,
            float defaultBudget, L4Mode l4Mode, string[] defaultExcludeKeys, ILogSink? logSink)
        {
            RegisterInternal(
                new ScenarioDescriptor
                {
                    Id = scenarioId,
                    DefaultBaseRounds = defaultBaseRounds,
                    Description = description,
                    DefaultBudget = defaultBudget,
                    L4Mode = l4Mode,
                    DefaultExcludeKeys = defaultExcludeKeys
                },
                logSink,
                isCoreFallback: true);
        }

        private static string Translate(ITranslationService? translationService, string key)
        {
            return translationService?.Translate(key) ?? key;
        }

        public static int GetBaseRounds(string scenarioId)
        {
            var meta = Get(scenarioId);
            return meta?.DefaultBaseRounds ?? 6;
        }

        public static void Clear()
        {
            _scenarios.Clear();
            _relevanceWeights.Clear();
            _coreRegistered = false;
        }
    }
}
