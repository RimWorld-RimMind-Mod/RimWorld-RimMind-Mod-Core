using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Behavior recording methods for an agent — goal removal and behavior record persistence.
    /// </summary>
    public interface IAgentBehavior
    {
        bool RemoveGoal(string goalDescription);
        void RecordBehavior(BehaviorRecordDto record);
    }
}
