namespace RimMind.Domain.Events
{
    public enum AgentBusEventType
    {
        Perception,
        Decision,
        Goal,
        Action,
        Lifecycle,
        ModeChange,
        InnerVoice,
        Reflection,
        ScheduleUpdate,
        MoodThreshold,
        NeedCritical,
        MentalStateWarning,
        InformationDiffusion,
        SocialEventProposed,
        TraitEvolution,
        Dream,
        DecisionFailed,
        WorkflowPhaseChange
    }

    public class AgentBusEvent
    {
        public string NpcId = "";
        public int PawnId;
        public AgentBusEventType BusEventType;
        public int Timestamp;

        public AgentBusEvent() { }

        public AgentBusEvent(string npcId, int pawnId, AgentBusEventType busEventType, int timestamp = 0)
        {
            NpcId = npcId;
            PawnId = pawnId;
            BusEventType = busEventType;
            Timestamp = timestamp;
        }
    }
}
