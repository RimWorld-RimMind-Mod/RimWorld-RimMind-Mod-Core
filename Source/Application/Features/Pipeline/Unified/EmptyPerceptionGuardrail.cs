using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public sealed class EmptyPerceptionGuardrail : IInputGuardrail
    {
        public string Name => "empty_perception";

        public GuardrailResult Check(LlmRequestEnvelope envelope)
        {
            // If request already carries explicit chat messages (e.g. test connections, direct prompts), perception is not required
            if (envelope.Messages != null && envelope.Messages.Count > 0)
                return GuardrailResult.Ok();

            if (envelope.GameStateInfo == null || string.IsNullOrWhiteSpace(envelope.GameStateInfo.ToXml()))
                return GuardrailResult.Fail("GameStateInfo is empty - no perception data for decision");
            return GuardrailResult.Ok();
        }
    }
}
