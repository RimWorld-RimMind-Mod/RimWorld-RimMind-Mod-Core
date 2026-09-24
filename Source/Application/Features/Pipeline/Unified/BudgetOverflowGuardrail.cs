using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public sealed class BudgetOverflowGuardrail : IInputGuardrail
    {
        public string Name => "budget_overflow";
        private const int MaxGameStateInfoLength = 8000;

        public GuardrailResult Check(LlmRequestEnvelope envelope)
        {
            var gsi = envelope.GameStateInfo;
            if (gsi != null)
            {
                var xml = gsi.ToXml();
                if (xml.Length > MaxGameStateInfoLength)
                    return GuardrailResult.Fail($"GameStateInfo exceeds {MaxGameStateInfoLength} chars ({xml.Length})");
            }
            return GuardrailResult.Ok();
        }
    }
}
