using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IEnvelopeEnricher
    {
        int Order { get; }
        void Enrich(LlmRequestEnvelope envelope, IAgentInfo agent);
    }
}
