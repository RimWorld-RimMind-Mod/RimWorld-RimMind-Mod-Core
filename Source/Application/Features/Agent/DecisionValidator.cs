using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Agent.Modes;
using RimMind.Application.Common.Interfaces.Tools;

namespace RimMind.Application.Features.Agent
{
    public class DecisionValidator : IDecisionValidator
    {
        public ValidationResult Validate(AgentDecision decision, IToolRegistry toolRegistry)
        {
            if (decision == null)
                return ValidationResult.Fail("Decision is null");

            if (string.IsNullOrEmpty(decision.ActionIntent))
                return ValidationResult.Fail("ActionIntent is empty");

            if (decision.ActionIntent == "dialogue.free")
                return ValidationResult.Ok();

            var dotIndex = decision.ActionIntent.IndexOf('.');
            if (dotIndex < 0 || dotIndex >= decision.ActionIntent.Length - 1)
                return ValidationResult.Fail($"ActionIntent '{decision.ActionIntent}' does not follow mechanism.action format");

            var mechanismId = decision.ActionIntent.Substring(0, dotIndex);
            var actionSuffix = decision.ActionIntent.Substring(dotIndex + 1);

            var handler = toolRegistry.FindById(decision.ActionIntent);
            if (handler == null)
            {
                var mechanismExists = toolRegistry.All.Any(h => h.Id.StartsWith(mechanismId + "."));
                if (!mechanismExists)
                    return ValidationResult.Fail($"No mechanism found for '{mechanismId}' in ActionIntent '{decision.ActionIntent}'");

                var supportedSuffixes = toolRegistry.All
                    .Where(h => h.Id.StartsWith(mechanismId + "."))
                    .Select(h => h.Id.Substring(mechanismId.Length + 1))
                    .ToList();
                return ValidationResult.Fail($"Operation '{actionSuffix}' not supported by mechanism '{mechanismId}'. Supported: {string.Join(", ", supportedSuffixes)}");
            }

            if (decision.Param is { Length: > 0 } param)
            {
                try { JToken.Parse(param); }
                catch (JsonReaderException) { return ValidationResult.Fail($"Param is not valid JSON: {param}"); }
            }

            return ValidationResult.Ok();
        }
    }
}
