using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public sealed class ToolRegistryDebugTableModelBuilder : IDebugTableModelBuilder
    {
        private static readonly string[] FullHeaders =
        {
            "RimMind.UI.DebugTable.Header.Id",
            "RimMind.UI.DebugTable.Header.Status",
            "RimMind.UI.DebugTable.Header.Category",
            "RimMind.UI.DebugTable.Header.OwnerMod",
            "RimMind.UI.DebugTable.Header.Kind",
            "RimMind.UI.DebugTable.Header.Title",
            "RimMind.UI.DebugTable.Header.Domain",
            "RimMind.UI.DebugTable.Header.Summary"
        };

        private static readonly string[] CompactHeaders =
        {
            "RimMind.UI.DebugTable.Header.Tool",
            "RimMind.UI.DebugTable.Header.Summary"
        };

        private IToolRegistry? _registry;
        private long _cachedGeneration = long.MinValue;
        private int _cachedCount = -1;
        private DebugTableModel? _cachedModel;

        public ToolRegistryDebugTableModelBuilder(IToolRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Bind(IToolRegistry? registry, long generation)
        {
            if (_cachedGeneration == generation && ReferenceEquals(_registry, registry))
                return;

            _registry = registry;
            _cachedGeneration = generation;
            _cachedCount = -1;
            _cachedModel = null;
        }

        public DebugTableModel Build()
        {
            int currentCount = _registry?.All.Count ?? 0;
            if (_cachedModel != null && _cachedCount == currentCount)
                return _cachedModel;

            _cachedModel = Build(_registry);
            _cachedCount = currentCount;
            return _cachedModel;
        }

        public static IReadOnlyList<ToolDefinition> FilterDefinitions(
            IReadOnlyList<ToolDefinition>? definitions,
            string? categoryFilter = null,
            string? searchQuery = null)
        {
            if (definitions == null || definitions.Count == 0)
                return Array.Empty<ToolDefinition>();

            IEnumerable<ToolDefinition> query = definitions.Where(d => d != null);

            if (!string.IsNullOrWhiteSpace(categoryFilter)
                && !string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                string cat = categoryFilter!.Trim();
                query = query.Where(d =>
                    string.Equals(d.Category, cat, StringComparison.OrdinalIgnoreCase)
                    || (d.Manifest != null && string.Equals(d.Manifest.Kind.ToString(), cat, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string q = searchQuery!.Trim();
                query = query.Where(d =>
                    ContainsIgnoreCase(d.Id, q)
                    || ContainsIgnoreCase(d.Category, q)
                    || ContainsIgnoreCase(d.Description, q)
                    || ContainsIgnoreCase(d.ParametersSchema, q)
                    || ContainsIgnoreCase(d.Manifest?.DisplayName, q)
                    || ContainsIgnoreCase(d.Manifest?.OwnerModId, q)
                    || ContainsIgnoreCase(d.Manifest?.DefaultDomain?.ToString(), q));
            }

            return query
                .OrderBy(d => d.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static DebugTableModel Build(
            IToolRegistry? registry,
            string? categoryFilter = null,
            string? searchQuery = null)
        {
            string title = "RimMind.UI.Hub.Tab.ToolCalls".CanTranslate()
                ? "RimMind.UI.Hub.Tab.ToolCalls".Translate().ToString()
                : "Tool Calls";

            if (registry == null)
                return new DebugTableModel(title, Array.Empty<DebugTableRow>(), FullHeaders, CompactHeaders);

            IReadOnlyList<ToolDefinition> defs = FilterDefinitions(
                registry.GetAllDefinitions(),
                categoryFilter,
                searchQuery);

            return new DebugTableModel(title, defs.Select(ToRow), FullHeaders, CompactHeaders);
        }

        private static DebugTableRow ToRow(ToolDefinition def)
        {
            ToolManifest? manifest = def.Manifest;
            string title = manifest != null && !string.IsNullOrWhiteSpace(manifest.DisplayName)
                ? $"{manifest.DisplayName} ({def.Id})"
                : def.Id;
            string domain = manifest?.DefaultDomain?.ToString() ?? "-";
            string risk = manifest != null
                ? (manifest.RequiresApproval ? $"{manifest.RiskLevel}*" : manifest.RiskLevel.ToString())
                : "-";

            return DebugTableRow.Create(
                def.Id,
                DebugTableStatus.Completed,
                def.Category ?? string.Empty,
                manifest?.OwnerModId ?? string.Empty,
                manifest != null ? manifest.Kind.ToString() : string.Empty,
                DebugTableText.Preview(title),
                domain,
                DebugTableText.Preview(def.Description ?? string.Empty),
                risk);
        }

        private static bool ContainsIgnoreCase(string? source, string value)
            => !string.IsNullOrEmpty(source) && source!.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
