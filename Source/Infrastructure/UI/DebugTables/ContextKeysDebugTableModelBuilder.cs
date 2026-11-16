using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public sealed class ContextKeysDebugTableModelBuilder : IDebugTableModelBuilder
    {
        private static readonly string[] Headers =
        {
            "RimMind.UI.DebugTable.Header.Id",
            "RimMind.UI.DebugTable.Header.Status",
            "RimMind.UI.DebugTable.Header.Score",
            "RimMind.UI.DebugTable.Header.Layer",
            "RimMind.UI.DebugTable.Header.OwnerMod",
            "RimMind.UI.DebugTable.Header.CacheScope",
            "RimMind.UI.DebugTable.Header.Category",
            "RimMind.UI.DebugTable.Header.Summary"
        };

        private IContextKeyRegistry? _registry;
        private long _cachedGeneration = long.MinValue;
        private DebugTableModel? _cachedModel;

        public ContextKeysDebugTableModelBuilder(IContextKeyRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Bind(IContextKeyRegistry? registry, long generation)
        {
            if (_cachedGeneration == generation && ReferenceEquals(_registry, registry))
                return;
            _registry = registry;
            _cachedGeneration = generation;
            _cachedModel = null;
        }

        public DebugTableModel Build()
            => _cachedModel ??= Build(_registry?.GetAll() ?? System.Array.Empty<KeyMeta>());

        public static DebugTableModel Build(IReadOnlyList<KeyMeta> keys)
        {
            return new DebugTableModel(
                "RimMind.UI.Hub.Tab.ContextKeys".Translate(),
                keys.Select(ToRow),
                Headers);
        }

        private static DebugTableRow ToRow(KeyMeta key)
        {
            string priority = key.Priority.ToString("0.###", CultureInfo.InvariantCulture);
            string effectivePriority = key.GetEffectivePriority().ToString("0.###", CultureInfo.InvariantCulture);
            string scoreText = key.CurrentScore.ToString("0.###", CultureInfo.InvariantCulture);
            string summary = "RimMind.UI.DebugTable.ContextKeys.Priority".Translate(priority).ToString()
                + " | " + "RimMind.UI.DebugTable.ContextKeys.Effective".Translate(effectivePriority).ToString()
                + " | " + "RimMind.UI.DebugTable.ContextKeys.UpdateCount".Translate(key.UpdateCount).ToString();

            return DebugTableRow.Create(
                key.Key,
                DebugTableStatus.Completed,
                scoreText,
                DebugTableEnumLabels.For(key.Layer),
                key.OwnerMod ?? string.Empty,
                DebugTableEnumLabels.For(key.CacheScope),
                "RimMind.UI.DebugTable.ContextKeys.Model".Translate().ToString(),
                summary,
                "RimMind.UI.DebugTable.ContextKeys.Score"
                    .Translate(scoreText)
                    .ToString());
        }
    }
}
