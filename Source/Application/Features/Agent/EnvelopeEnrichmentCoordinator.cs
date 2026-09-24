using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    internal class EnvelopeEnrichmentCoordinator
    {
        private readonly List<IEnvelopeEnricher> _enrichers;

        public EnvelopeEnrichmentCoordinator(IEnumerable<IEnvelopeEnricher> enrichers)
        {
            _enrichers = (enrichers ?? Enumerable.Empty<IEnvelopeEnricher>())
                .OrderBy(e => e.Order).ToList();
        }

        public void EnrichAll(LlmRequestEnvelope envelope, IAgentInfo agent)
        {
            if (envelope == null || agent == null) return;
            foreach (var enricher in _enrichers)
                enricher.Enrich(envelope, agent);
        }
    }
}
