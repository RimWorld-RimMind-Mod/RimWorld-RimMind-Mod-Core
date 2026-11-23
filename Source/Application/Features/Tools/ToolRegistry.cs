using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Registry;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;

namespace RimMind.Application.Features.Tools
{
    public sealed class ToolRegistry : IToolRegistry, IOwnedRegistry
    {
        private readonly object _indexSync = new object();
        private readonly ConcurrentDictionary<string, IToolHandler> _handlers
            = new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, IToolHandler> _normalizedAliases
            = new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, IToolHandler> _suffixIndex
            = new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);

        public ToolRegistry(ILogSink? log = null) { }

        public void Register(IToolHandler handler)
        {
            if (handler == null) return;

            lock (_indexSync)
            {
                var canonicalId = handler.Definition.Id;
                _handlers[canonicalId] = handler;
                IndexHandlerLocked(handler);
            }
        }

        public bool Unregister(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return false;
            lock (_indexSync)
            {
                bool removed = _handlers.TryRemove(toolId, out _);
                if (removed)
                {
                    RebuildSecondaryIndexesLocked();
                }
                return removed;
            }
        }

        /// <inheritdoc/>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            lock (_indexSync)
            {
                var toRemove = _handlers.Values
                    .Where(h => h.OwnerModId == ownerModId)
                    .ToList();
                foreach (var handler in toRemove)
                {
                    _handlers.TryRemove(handler.Definition.Id, out _);
                }
                if (toRemove.Count > 0)
                {
                    RebuildSecondaryIndexesLocked();
                }
                return toRemove.Count;
            }
        }

        private void IndexHandlerLocked(IToolHandler handler)
        {
            var canonicalId = handler.Definition.Id;
            var normalizedCanonical = canonicalId.Replace('.', '_');
            _normalizedAliases[normalizedCanonical] = handler;

            int sepIndex = normalizedCanonical.IndexOf('_');
            if (sepIndex > 0 && sepIndex + 1 < normalizedCanonical.Length)
            {
                var suffix = normalizedCanonical.Substring(sepIndex + 1);
                _suffixIndex[suffix] = handler;
            }

            var aliases = handler.Definition.Manifest?.Aliases;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Count; i++)
                {
                    var alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var trimmed = alias.Trim();
                    _normalizedAliases[trimmed] = handler;
                    _normalizedAliases[trimmed.Replace('.', '_')] = handler;
                }
            }
        }

        private void RebuildSecondaryIndexesLocked()
        {
            _normalizedAliases.Clear();
            _suffixIndex.Clear();
            foreach (var handler in _handlers.Values)
            {
                IndexHandlerLocked(handler);
            }
        }

        public IToolHandler? FindById(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId)) return null;
            var trimmed = toolId.Trim();

            if (_handlers.TryGetValue(trimmed, out var h)) return h;

            var targetNormalized = trimmed.Replace('.', '_');
            if (_normalizedAliases.TryGetValue(trimmed, out h)) return h;
            if (_normalizedAliases.TryGetValue(targetNormalized, out h)) return h;
            if (_suffixIndex.TryGetValue(targetNormalized, out h)) return h;

            return null;
        }

        public IReadOnlyList<IToolHandler> All => _handlers.Values.ToList();

        public IReadOnlyList<ToolDefinition> GetAllDefinitions()
        {
            return _handlers.Values.Select(h => h.Definition).ToList();
        }

        public IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind)
        {
            return _handlers.Values
                .Where(h => h.Definition.Manifest?.AllowedScopes?.Contains(scopeKind) == true)
                .ToList();
        }

        public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind)
        {
            return GetHandlersForScope(scopeKind)
                .Select(h => h.Definition)
                .ToList();
        }

        public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(ToolScope scope, bool compactSchema = false)
        {
            if (scope == ToolScope.None)
                return Array.Empty<ToolDefinition>();

            var matched = _handlers.Values
                .Select(h => h.Definition)
                .Where(d => ToolScopeProjection.MatchesScope(d, scope));

            return compactSchema
                ? matched.Select(ToolScopeProjection.CompactDefinition).ToList()
                : matched.ToList();
        }
    }
}
