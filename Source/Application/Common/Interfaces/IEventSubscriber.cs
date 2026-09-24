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
        [Obsolete("Use Unsubscribe<T>(string key) instead. Action-based unsubscribe is unreliable due to lambda wrapping.")]
        void Unsubscribe<T>(Action<T> handler) where T : AgentBusEvent;
        string SubscribeByName(string eventTypeName, Action<AgentBusEvent> handler);
    }
}
