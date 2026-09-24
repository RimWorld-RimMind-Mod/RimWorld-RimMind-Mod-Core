using System.Collections.Generic;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;

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
}
