using System.Collections.Generic;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Defaults
{
    public sealed class NullAgentActionBridge : IAgentActionBridge
    {
        public static readonly NullAgentActionBridge Instance = new NullAgentActionBridge();

        public string Id => "null-agent-action-bridge";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        public void ExecuteAction(string npcId, string actionName, string[]? args = null) { }
        public bool CanExecute(string npcId, string actionName) => false;
        public bool CanExecute(object pawn, string action) => false;
        public void Execute(object pawn, string action, string? targetName = null) { }
        public List<StructuredTool>? GetAvailableTools(object pawn) => null;
    }
}
