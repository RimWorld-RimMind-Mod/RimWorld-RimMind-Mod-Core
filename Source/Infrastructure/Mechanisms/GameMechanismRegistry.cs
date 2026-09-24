using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Registry;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Enums;

namespace RimMind.Infrastructure.Mechanisms
{
    public sealed class GameMechanismRegistry : IGameMechanismRegistry, IOwnedRegistry
    {
        private readonly ConcurrentDictionary<string, IGameMechanism> _mechanisms = new();
        private readonly IToolRegistry? _toolRegistry;
        private readonly IJsonExtractor? _jsonExtractor;

        public GameMechanismRegistry(IToolRegistry? toolRegistry = null, IJsonExtractor? jsonExtractor = null)
        {
            _toolRegistry = toolRegistry;
            _jsonExtractor = jsonExtractor;
        }

        public void Register(IGameMechanism mechanism)
        {
            if (mechanism == null) return;
            _mechanisms[mechanism.MechanismId] = mechanism;

            if (_toolRegistry != null)
            {
                foreach (var op in mechanism.SupportedOperations)
                {
                    var handler = new MechanismToolHandler(mechanism, mechanism, mechanism, mechanism, op, _jsonExtractor!);
                    _toolRegistry.Register(handler);
                }

                if (mechanism.SupportedOperations.Contains(MechanismOperationType.List))
                {
                    var listHandler = new MechanismListToolHandler(mechanism, mechanism, _jsonExtractor!);
                    _toolRegistry.Register(listHandler);
                }
            }
        }

        public bool Unregister(string mechanismId)
        {
            if (!_mechanisms.TryRemove(mechanismId, out var mechanism)) return false;

            if (_toolRegistry != null)
            {
                foreach (var op in mechanism.SupportedOperations)
                {
                    var toolId = $"{mechanism.MechanismId}.{OperationSuffix(op)}";
                    _toolRegistry.Unregister(toolId);
                }

                var listToolId = $"{mechanismId}.list";
                _toolRegistry.Unregister(listToolId);
            }

            return true;
        }

        /// <inheritdoc/>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            var toRemove = _mechanisms.Values
                .Where(m => m.OwnerModId == ownerModId)
                .Select(m => m.MechanismId)
                .ToList();
            foreach (var id in toRemove)
            {
                // 复用 Unregister 以同步清理 _toolRegistry 中的关联 ToolHandler
                Unregister(id);
            }
            return toRemove.Count;
        }

        public IGameMechanism? FindById(string mechanismId)
        {
            return _mechanisms.TryGetValue(mechanismId, out var mechanism) ? mechanism : null;
        }

        public IReadOnlyList<IGameMechanism> All => _mechanisms.Values.ToList().AsReadOnly();

        private static string OperationSuffix(MechanismOperationType operation)
        {
            return operation switch
            {
                MechanismOperationType.Query => "query",
                MechanismOperationType.Set => "set",
                MechanismOperationType.Add => "add",
                MechanismOperationType.Remove => "remove",
                MechanismOperationType.Toggle => "toggle",
                MechanismOperationType.Trigger => "trigger",
                MechanismOperationType.List => "list",
                MechanismOperationType.Watch => "watch",
                _ => operation.ToString().ToLowerInvariant()
            };
        }

    }
}
