using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Models.Agent
{
    public class AgentGoalStack
    {
        private const int MaxActiveGoals = 3;
        private const int MaxTotalGoals = 10;

        protected readonly List<AgentGoal> _goals = new List<AgentGoal>();
        private IAgentBus? _agentBus;

        private List<AgentGoal>? _activeGoalsCache;
        private int _version;
        private int _activeGoalsCacheVersion;
        private int _activeCount;

        public AgentGoalStack() { }

        public AgentGoalStack(IAgentBus agentBus)
        {
            _agentBus = agentBus;
        }

        public void SetAgentBus(IAgentBus agentBus)
        {
            _agentBus = agentBus;
        }

        public IReadOnlyList<AgentGoal> Goals => _goals;
        public IReadOnlyList<AgentGoal> ActiveGoals
        {
            get
            {
                if (_activeGoalsCache == null || _activeGoalsCacheVersion != _version)
                {
                    _activeGoalsCache = _goals.Where(g => g.Status == GoalStatus.Active)
                        .OrderByDescending(g => g.Priority).ToList();
                    _activeGoalsCacheVersion = _version;
                }
                return _activeGoalsCache;
            }
        }
        public int ActiveCount => _activeCount;
        public int TotalCount => _goals.Count;

        public bool TryAdd(AgentGoal goal, int pawnId)
        {
            if (goal == null) return false;
            if (TotalCount >= MaxTotalGoals)
            {
                var removable = _goals.FirstOrDefault(g => g.Status == GoalStatus.Proposed);
                if (removable == null) return false;
                if (removable.Status == GoalStatus.Active) _activeCount--;
                _goals.Remove(removable);
            }
            if (ActiveCount < MaxActiveGoals && goal.Status == GoalStatus.Proposed)
            {
                goal.Status = GoalStatus.Active;
                _activeCount++;
            }
            _goals.Add(goal);
            BumpVersion();
            _goals.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _agentBus?.Publish(new GoalEvent(
                $"NPC-{pawnId}", pawnId, goal.Description, goal.Status.ToString(), goal.Category.ToString()));
            return true;
        }

        public bool Remove(string description, int pawnId)
        {
            int idx = _goals.FindIndex(g => g.Description == description);
            if (idx < 0) return false;
            var goal = _goals[idx];
            if (goal.Status == GoalStatus.Active) _activeCount--;
            _goals.RemoveAt(idx);
            BumpVersion();
            _agentBus?.Publish(new GoalEvent(
                $"NPC-{pawnId}", pawnId, goal.Description, GoalStatus.Abandoned.ToString(), goal.Category.ToString()));
            PromoteProposed();
            return true;
        }

        public void CheckExpired(int pawnId, int currentTick)
        {
            for (int i = _goals.Count - 1; i >= 0; i--)
            {
                if (_goals[i].IsExpired(currentTick))
                {
                    var goal = _goals[i];
                    if (goal.Status == GoalStatus.Active) _activeCount--;
                    goal.Status = GoalStatus.Expired;
                    _goals.RemoveAt(i);
                    BumpVersion();
                    _agentBus?.Publish(new GoalEvent(
                        $"NPC-{pawnId}", pawnId, goal.Description, GoalStatus.Expired.ToString(), goal.Category.ToString()));
                }
            }
            PromoteProposed();
        }

        private void PromoteProposed()
        {
            while (ActiveCount < MaxActiveGoals)
            {
                var proposed = _goals.FirstOrDefault(g => g.Status == GoalStatus.Proposed);
                if (proposed == null) break;
                proposed.Status = GoalStatus.Active;
                _activeCount++;
                BumpVersion();
            }
        }

        public void Clear()
        {
            _goals.Clear();
            _activeCount = 0;
        }

        private void BumpVersion() => _version++;
    }
}
