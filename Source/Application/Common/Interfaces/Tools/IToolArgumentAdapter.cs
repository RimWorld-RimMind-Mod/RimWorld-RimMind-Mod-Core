using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Common.Interfaces.Tools
{
    /// <summary>
    /// Allows an extension module to adapt raw tool arguments JSON from an <see cref="AgentDecision"/>
    /// before tool dispatch without hard-coding module-specific tool heuristics in Core.
    /// </summary>
    public interface IToolArgumentAdapter : IExtension
    {
        bool CanAdapt(string toolId);

        JObject AdaptArguments(string toolId, JObject args, AgentDecision decision, int callerPawnId);
    }
}
