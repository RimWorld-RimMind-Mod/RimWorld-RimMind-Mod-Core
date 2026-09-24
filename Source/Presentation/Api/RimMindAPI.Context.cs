using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Facade for context-related operations: ScenarioRegistry, ContextKeyRegistry, SchemaRegistry.
        /// Sub-mods should use this instead of directly referencing
        /// RimMind.Application.Features.Context or RimMind.Presentation.Context.
        /// </summary>
        public static class Context
        {
            private static readonly RuntimeServiceRef<IContextKeyRegistry> ContextKeyRegistries =
                RuntimeServiceRef<IContextKeyRegistry>.Required();
            private static readonly RuntimeServiceRef<IRelevanceTable> RelevanceTables =
                RuntimeServiceRef<IRelevanceTable>.Required();
            private static readonly RuntimeServiceRef<IRelevanceLearner> ContextLearners =
                RuntimeServiceRef<IRelevanceLearner>.Required();
            private static readonly RuntimeServiceRef<SchemaRegistry> SchemaRegistries =
                RuntimeServiceRef<SchemaRegistry>.Required();
            // ── ScenarioIds ──

            public static string ScenarioDialogue => ScenarioIds.Dialogue;
            public static string ScenarioDecision => ScenarioIds.Decision;
            public static string ScenarioPersonality => ScenarioIds.Personality;
            public static string ScenarioStoryteller => ScenarioIds.Storyteller;
            public static string ScenarioMemory => ScenarioIds.Memory;

            // ── ScenarioRegistry ──

            public static void RegisterScenario(string scenarioId, int defaultBaseRounds, string description,
                float[]? defaultEmbedding = null, float defaultBudget = 0.6f,
                L4Mode l4Mode = L4Mode.BudgetControlled, string[]? defaultExcludeKeys = null)
            {
                var scope = RuntimeServiceHub.Shared.Capture();
                ScenarioRegistry.Register(scenarioId, defaultBaseRounds, description,
                    defaultEmbedding, defaultBudget, l4Mode, defaultExcludeKeys,
                    scope.GetRequired<ILogSink>());
            }

            public static bool UnregisterScenario(string scenarioId)
                => ScenarioRegistry.Unregister(scenarioId);

            public static int GetScenarioBaseRounds(string scenarioId)
                => ScenarioRegistry.GetBaseRounds(scenarioId);

            // ── ContextKeyRegistry (instance-based via RimMindRuntime) ──

            public static IContextKeyRegistry ContextKeys => ContextKeyRegistries.Value;
            public static IRelevanceTable RelevanceTable => RelevanceTables.Value;
            public static IRelevanceLearner ContextLearner => ContextLearners.Value;

            // ── SchemaRegistry ──

            private static SchemaRegistry Schemas => SchemaRegistries.Value;

            public static string SchemaPersonalityOutput
                => Schemas.PersonalityOutput;

            public static string SchemaIncidentOutput
                => Schemas.IncidentOutput;

            public static string SchemaDarkMemoryOutput
                => Schemas.DarkMemoryOutput;

            public static void RegisterSchema(string key, string schema)
                => Schemas.Register(key, schema);

            public static string? FindSchema(string key)
                => Schemas.Find(key);
        }
    }
}
