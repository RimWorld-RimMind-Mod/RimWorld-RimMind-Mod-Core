using System;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Interfaces
{
    public interface IEventSubscriber
    {
        string Subscribe<T>(Action<T> handler) where T : AgentBusEvent;
        void Subscribe<T>(string key, Action<T> handler) where T : AgentBusEvent;
        void Unsubscribe<T>(string key) where T : AgentBusEvent;
        void Unsubscribe(string key);
        string SubscribeByName(string eventTypeName, Action<AgentBusEvent> handler);
    }
}
