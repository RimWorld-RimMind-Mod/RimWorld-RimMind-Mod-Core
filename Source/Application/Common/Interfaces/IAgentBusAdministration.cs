using System;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Common;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Interfaces
{
    public interface IAgentBusAdministration
    {
        event Action? SubscribersCleared;

        void SetPipeline(IPipeline<BusPublishContext> pipeline);

        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        void FlushBackgroundQueue();

        void ClearAllSubscribers();

        [ThreadAffinity(ThreadAffinityKind.Any)]
        int GetHandlerCount();

        [ThreadAffinity(ThreadAffinityKind.Any)]
        int GetBackgroundQueueCount();

        Action<AgentBusEvent>? DispatchAction { get; }

        /// <summary>
        /// Register a custom event type mapping so SubscribeByName can resolve it.
        /// Built-in event types are pre-registered; use this for custom event types from sub-mods.
        /// </summary>
        void RegisterEventType(string name, Type eventType);
    }
}
