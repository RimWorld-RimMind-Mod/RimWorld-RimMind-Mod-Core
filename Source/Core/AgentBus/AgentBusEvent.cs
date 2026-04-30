using System;
using Verse;

namespace RimMind.Core.AgentBus
{
    public abstract class AgentBusEvent
    {
        public AgentBusEventType EventType { get; }
        public int Timestamp { get; internal set; }
        public string SourceNpcId { get; }

        protected AgentBusEvent(AgentBusEventType eventType, string sourceNpcId = "")
        {
            EventType = eventType;
            Timestamp = 0;
            SourceNpcId = sourceNpcId ?? "";
        }
    }
}
