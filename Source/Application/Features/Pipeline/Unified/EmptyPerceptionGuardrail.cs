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

            // If request targets a specific NpcId and omits GameStateInfo, ContextBuildMiddleware (Order=40) will build L0..L5 context
            if (!string.IsNullOrWhiteSpace(envelope.NpcId) && envelope.GameStateInfo == null)
                return GuardrailResult.Ok();

            if (envelope.GameStateInfo == null || string.IsNullOrWhiteSpace(envelope.GameStateInfo.ToXml()))
                return GuardrailResult.Fail("GameStateInfo is empty - no perception data for decision");
            return GuardrailResult.Ok();
        }
    }
}
