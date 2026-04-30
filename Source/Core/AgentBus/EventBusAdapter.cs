using System;

namespace RimMind.Core.AgentBus
{
    public class EventBusAdapter : IEventBus
    {
        public void Subscribe<T>(Action<T> handler) where T : AgentBusEvent
            => AgentBus.Subscribe(handler);

        public void Unsubscribe<T>(Action<T> handler) where T : AgentBusEvent
            => AgentBus.Unsubscribe(handler);

        public void Publish<T>(T evt) where T : AgentBusEvent
            => AgentBus.Publish(evt);

        public void PublishFromBackground<T>(T evt) where T : AgentBusEvent
            => AgentBus.PublishFromBackground(evt);

        public void FlushBackgroundQueue()
            => AgentBus.FlushBackgroundQueue();
    }
}
