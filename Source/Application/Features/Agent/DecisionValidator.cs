using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Agent.Modes;
using RimMind.Application.Common.Interfaces.Tools;

namespace RimMind.Application.Features.Agent
{
    public sealed record ValidationResult(bool IsValid, string Reason = "")
    {
        public static ValidationResult Ok() => new(true);
        public static ValidationResult Fail(string reason) => new(false, reason);
    }

    public class DecisionValidator
    {
        public ValidationResult Validate(AgentDecision decision, IToolRegistry toolRegistry)
        {
            if (decision == null)
                return ValidationResult.Fail("Decision is null");

            if (string.IsNullOrEmpty(decision.ActionIntent))
                return ValidationResult.Fail("ActionIntent is empty");

            if (decision.ActionIntent == "dialogue.free")
                return ValidationResult.Ok();

            var handler = toolRegistry.FindById(decision.ActionIntent);
            if (handler == null)
            {
                var dotIndex = decision.ActionIntent.LastIndexOf('.');
                if (dotIndex <= 0 || dotIndex >= decision.ActionIntent.Length - 1)
                    return ValidationResult.Fail($"Unknown tool or ActionIntent '{decision.ActionIntent}'");

                var mechanismId = decision.ActionIntent.Substring(0, dotIndex);
                var actionSuffix = decision.ActionIntent.Substring(dotIndex + 1);
                string prefix = mechanismId + ".";

                var matchingHandlers = toolRegistry.All
                    .Where(h => h.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matchingHandlers.Count == 0)
                    return ValidationResult.Fail($"No mechanism found for '{mechanismId}' in ActionIntent '{decision.ActionIntent}'");

                var supportedSuffixes = matchingHandlers
                    .Select(h => h.Id.Substring(prefix.Length))
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
