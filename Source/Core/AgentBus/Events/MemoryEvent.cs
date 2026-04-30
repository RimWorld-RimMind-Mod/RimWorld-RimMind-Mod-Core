namespace RimMind.Core.AgentBus
{
    public class MemoryEvent : AgentBusEvent
    {
        public string MemoryOperation { get; }
        public string MemoryKey { get; }
        public string MemoryType { get; }
        public int PawnId { get; }

        public MemoryEvent(string sourceNpcId, int pawnId, string memoryOperation, string memoryKey, string memoryType = "")
            : base(AgentBusEventType.Memory, sourceNpcId)
        {
            PawnId = pawnId;
            MemoryOperation = memoryOperation ?? "";
            MemoryKey = memoryKey ?? "";
            MemoryType = memoryType ?? "";
        }
    }
}
