using System;
using RimMind.Domain.Common;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Interfaces
{
    public interface IAgentBus : IEventPublisher, IEventSubscriber, IAgentBusAdministration
    {
    }
}
