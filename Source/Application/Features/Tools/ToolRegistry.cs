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
            = new ConcurrentDictionary<string, IToolHandler>();
        private readonly ILogSink? _log;

        public ToolRegistry(ILogSink? log = null) { _log = log; }

        public void Register(IToolHandler handler)
        {
            if (handler == null) return;
            _handlers[handler.Definition.Id] = handler;
        }

        public bool Unregister(string toolId)
        {
            return _handlers.TryRemove(toolId, out _);
        }

        /// <inheritdoc/>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            var toRemove = _handlers.Values
                .Where(h => h.OwnerModId == ownerModId)
                .Select(h => h.Definition.Id)
                .ToList();
            foreach (var id in toRemove)
            {
                _handlers.TryRemove(id, out _);
            }
            return toRemove.Count;
        }

        public IToolHandler? FindById(string toolId)
        {
            return _handlers.TryGetValue(toolId, out var h) ? h : null;
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
