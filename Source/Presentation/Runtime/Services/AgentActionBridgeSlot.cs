using System;
using System.Threading;
using RimMind.Application.Common.Defaults;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Presentation.Runtime.Services
{
    internal sealed class AgentActionBridgeSlot : IAgentActionBridgeAccessor
    {
        private IAgentActionBridge _current = NullAgentActionBridge.Instance;

        public IAgentActionBridge Current => Volatile.Read(ref _current);

        public void Replace(IAgentActionBridge bridge, ILogSink? logSink = null)
        {
            if (bridge == null)
            {
                throw new ArgumentNullException(nameof(bridge));
            }

            var previous = Interlocked.Exchange(ref _current, bridge);
            if (!ReferenceEquals(previous, NullAgentActionBridge.Instance))
            {
                logSink?.Warning(
                    $"[AgentActionBridgeSlot] event=agent_action_bridge_replaced " +
                    $"previous_id={previous.Id} previous_owner={previous.OwnerModId} " +
                    $"replacement_id={bridge.Id} replacement_owner={bridge.OwnerModId}");
            }
        }

        public void Reset()
        {
            Volatile.Write(ref _current, NullAgentActionBridge.Instance);
        }
    }
}
