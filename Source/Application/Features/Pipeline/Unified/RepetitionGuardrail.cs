using System;
using System.IO;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public sealed class RepetitionGuardrail : IInputGuardrail
    {
        public string Name => "repetition";
        private const int MaxConsecutiveRepeats = 4;

        public GuardrailResult Check(LlmRequestEnvelope envelope)
        {
            var gsi = envelope.GameStateInfo;
            if (gsi == null) return GuardrailResult.Ok();
            var xml = gsi.ToXml();
            if (string.IsNullOrEmpty(xml)) return GuardrailResult.Ok();

            using var reader = new StringReader(xml);
            string? line;
            string? lastLine = null;
            int consecutiveCount = 0;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (string.Equals(line, lastLine, StringComparison.OrdinalIgnoreCase))
                {
                    consecutiveCount++;
                    if (consecutiveCount >= MaxConsecutiveRepeats)
                    {
                        return GuardrailResult.Fail($"Perception line '{line}' repeated consecutively {consecutiveCount} times - possible loop");
                    }
                }
                else
                {
                    lastLine = line;
                    consecutiveCount = 1;
                }
            }

            return GuardrailResult.Ok();
        }
    }
}
