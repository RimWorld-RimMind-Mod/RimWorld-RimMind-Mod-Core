using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;

namespace RimMind.Application.Common.Interfaces.Tools
{
    public interface IToolRegistry
    {
        void Register(IToolHandler handler);
        bool Unregister(string toolId);
        IToolHandler? FindById(string toolId);
        IReadOnlyList<IToolHandler> All { get; }
        IReadOnlyList<ToolDefinition> GetAllDefinitions();
        IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind);
        IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind);
    }

    public static class ToolRegistryScopeExtensions
    {
        /// <summary>
        /// Projects tool definitions matching the requested <see cref="ToolScope"/> bitmask,
        /// optionally applying compact schema projection to cut LLM prompt token bloat.
        /// </summary>
        public static IReadOnlyList<ToolDefinition> GetDefinitionsForScope(
            this IToolRegistry? registry,
            ToolScope scope,
            bool compactSchema = false)
        {
            if (registry == null || scope == ToolScope.None)
                return System.Array.Empty<ToolDefinition>();

            if (registry is ToolRegistry concrete)
                return concrete.GetDefinitionsForScope(scope, compactSchema);

            var matched = registry.GetAllDefinitions()
                .Where(d => ToolScopeProjection.MatchesScope(d, scope));
            return compactSchema
                ? matched.Select(ToolScopeProjection.CompactDefinition).ToList()
                : matched.ToList();
        }
    }
}
