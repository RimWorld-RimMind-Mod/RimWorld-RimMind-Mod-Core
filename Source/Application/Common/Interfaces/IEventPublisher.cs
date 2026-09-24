using RimMind.Domain.Common;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Interfaces
{
    public interface IEventPublisher
    {
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        void Publish<T>(T evt) where T : AgentBusEvent;

        [ThreadAffinity(ThreadAffinityKind.Any)]
        void PublishFromBackground<T>(T evt) where T : AgentBusEvent;
    }
}
