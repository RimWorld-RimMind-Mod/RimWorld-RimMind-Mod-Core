using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Application.Features.Requests.Queue
{
    public sealed class AgentBusQueueTickCoordinator
    {
        private readonly IAgentBus _agentBus;
        private readonly ITickableRequestQueue _queue;

        public AgentBusQueueTickCoordinator(
            IAgentBus agentBus,
            ITickableRequestQueue queue)
        {
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public void Tick(int currentTick)
        {
            _queue.CurrentTick = currentTick;
            _agentBus.FlushBackgroundQueue();
            _queue.Tick();
        }
    }
}
