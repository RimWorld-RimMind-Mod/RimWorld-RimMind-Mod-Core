using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Pipeline.Unified
{
    public interface IInputGuardrail
    {
        string Name { get; }
        GuardrailResult Check(LlmRequestEnvelope envelope);
    }
}
