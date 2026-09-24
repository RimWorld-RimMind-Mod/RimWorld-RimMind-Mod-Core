using System.Collections.Generic;
using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Interfaces.Extension
{
    public interface IAgentActionBridge : IExtension
    {
        void ExecuteAction(string npcId, string actionName, string[]? args = null);
        bool CanExecute(string npcId, string actionName);
        bool CanExecute(object pawn, string action);
        void Execute(object pawn, string action, string? targetName = null);
        List<StructuredTool>? GetAvailableTools(object pawn);
    }
}
