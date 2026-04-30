using System;

namespace RimMind.Core.AgentBus
{
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler) where T : AgentBusEvent;
        void Unsubscribe<T>(Action<T> handler) where T : AgentBusEvent;
        void Publish<T>(T evt) where T : AgentBusEvent;
        void PublishFromBackground<T>(T evt) where T : AgentBusEvent;
        void FlushBackgroundQueue();
    }
}
