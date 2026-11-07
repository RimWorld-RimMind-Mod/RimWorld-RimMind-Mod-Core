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
        private readonly ConcurrentDictionary<string, IToolHandler> _handlers
            = new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, IToolHandler> _aliases
            = new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly ILogSink? _log;

        internal static IToolRegistry? Current { get; private set; }

        public ToolRegistry(ILogSink? log = null)
        {
            _log = log;
            Current = this;
        }

        public void Register(IToolHandler handler)
        {
            if (handler == null) return;
            Current = this;

            var canonicalId = handler.Definition.Id;
            _handlers[canonicalId] = handler;

            var aliases = handler.Definition.Manifest?.Aliases;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Count; i++)
                {
                    var alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var trimmed = alias.Trim();
                    _aliases[trimmed] = handler;
                    _aliases[trimmed.Replace('.', '_')] = handler;
                    _aliases[trimmed.Replace('_', '.')] = handler;
                }
            }
        }

        public bool Unregister(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return false;
            bool removed = _handlers.TryRemove(toolId, out var handler);
            if (removed && handler != null)
            {
                RemoveAliasesFor(handler);
            }
            return removed;
        }

        /// <inheritdoc/>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            var toRemove = _handlers.Values
                .Where(h => h.OwnerModId == ownerModId)
                .ToList();
            foreach (var handler in toRemove)
            {
                _handlers.TryRemove(handler.Definition.Id, out _);
                RemoveAliasesFor(handler);
            }
            return toRemove.Count;
        }

        private void RemoveAliasesFor(IToolHandler handler)
        {
            var aliasKeys = _aliases
                .Where(kvp => ReferenceEquals(kvp.Value, handler) ||
                              string.Equals(kvp.Value.Definition.Id, handler.Definition.Id, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();
            for (int i = 0; i < aliasKeys.Count; i++)
            {
                _aliases.TryRemove(aliasKeys[i], out _);
            }
        }

        public IToolHandler? FindById(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId)) return null;
            var trimmed = toolId.Trim();

            if (_handlers.TryGetValue(trimmed, out var h)) return h;
            if (_aliases.TryGetValue(trimmed, out h)) return h;

            if (trimmed.Contains("."))
            {
                var underscored = trimmed.Replace('.', '_');
                if (_handlers.TryGetValue(underscored, out h)) return h;
                if (_aliases.TryGetValue(underscored, out h)) return h;
            }
            if (trimmed.Contains("_"))
            {
                var dotted = trimmed.Replace('_', '.');
                if (_handlers.TryGetValue(dotted, out h)) return h;
                if (_aliases.TryGetValue(dotted, out h)) return h;
            }

            var targetNormalized = trimmed.Replace('.', '_');
            foreach (var kvp in _handlers)
            {
                if (string.Equals(kvp.Key.Replace('.', '_'), targetNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }

                var manifestAliases = kvp.Value.Definition?.Manifest?.Aliases;
                if (manifestAliases != null)
                {
                    for (int i = 0; i < manifestAliases.Count; i++)
                    {
                        var alias = manifestAliases[i];
                        if (!string.IsNullOrWhiteSpace(alias) &&
                            string.Equals(alias.Trim().Replace('.', '_'), targetNormalized, StringComparison.OrdinalIgnoreCase))
                        {
                            return kvp.Value;
                        }
                    }
                }
            }

            // Generic unqualified suffix match when a tool is registered under a module namespace prefix (e.g. "<module>_<name>" or "<module>.<name>")
            foreach (var kvp in _handlers)
            {
                var normalizedKey = kvp.Key.Replace('.', '_');
                int sepIndex = normalizedKey.IndexOf('_');
                if (sepIndex > 0 && sepIndex + 1 < normalizedKey.Length)
                {
                    var withoutPrefix = normalizedKey.Substring(sepIndex + 1);
                    if (string.Equals(withoutPrefix, targetNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }
            }

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
    }
}
