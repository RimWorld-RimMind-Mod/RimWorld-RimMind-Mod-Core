using System;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public sealed class RepetitionGuardrail : IInputGuardrail
    {
        public string Name => "repetition";
        private const int MaxIdenticalPrefix = 3;
        private static readonly string[] PerceptionPrefixes = { "[mood]", "[need]", "[combat]", "[health]", "[social]", "[environment]" };

        public GuardrailResult Check(LlmRequestEnvelope envelope)
        {
            var gsi = envelope.GameStateInfo;
            if (gsi == null) return GuardrailResult.Ok();
            var xml = gsi.ToXml();
            if (string.IsNullOrEmpty(xml)) return GuardrailResult.Ok();

            foreach (var prefix in PerceptionPrefixes)
            {
                var count = 0;
                var idx = 0;
                while ((idx = xml.IndexOf(prefix, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    count++;
                    idx += prefix.Length;
                }
                if (count >= MaxIdenticalPrefix)
                    return GuardrailResult.Fail($"Perception prefix '{prefix}' appears {count} times - possible loop");
            }
            return GuardrailResult.Ok();
        }
    }
}
