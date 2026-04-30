using System.Collections.Generic;
using System.Linq;
using RimMind.Core.AgentBus;
using Verse;

namespace RimMind.Core.Agent
{
    public class AgentGoalStack : IExposable
    {
        private const int MaxActiveGoals = 3;
        private const int MaxTotalGoals = 10;

        private readonly List<AgentGoal> _goals = new List<AgentGoal>();

        private List<AgentGoal>? _activeGoalsCache;
        private int _version;
        private int _activeGoalsCacheVersion;

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
        public int ActiveCount => _goals.Count(g => g.Status == GoalStatus.Active);
        public int TotalCount => _goals.Count;

        public bool TryAdd(AgentGoal goal, int pawnId)
        {
            if (goal == null) return false;
            if (TotalCount >= MaxTotalGoals)
            {
                var removable = _goals.FirstOrDefault(g => g.Status == GoalStatus.Proposed);
                if (removable == null) return false;
                _goals.Remove(removable);
            }
            if (ActiveCount < MaxActiveGoals && goal.Status == GoalStatus.Proposed)
                goal.Status = GoalStatus.Active;
            _goals.Add(goal);
            BumpVersion();
            _goals.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            global::RimMind.Core.AgentBus.AgentBus.Publish(new GoalEvent(
                $"NPC-{pawnId}", pawnId, goal.Description, goal.Status.ToString(), goal.Category.ToString()));
            return true;
        }

        public bool Remove(string description, int pawnId)
        {
            int idx = _goals.FindIndex(g => g.Description == description);
            if (idx < 0) return false;
            var goal = _goals[idx];
            _goals.RemoveAt(idx);
            BumpVersion();
            global::RimMind.Core.AgentBus.AgentBus.Publish(new GoalEvent(
                $"NPC-{pawnId}", pawnId, goal.Description, GoalStatus.Abandoned.ToString(), goal.Category.ToString()));
            PromoteProposed();
            return true;
        }

        public void CheckExpired(int pawnId)
        {
            for (int i = _goals.Count - 1; i >= 0; i--)
            {
                if (_goals[i].IsExpired)
                {
                    var goal = _goals[i];
                    goal.Status = GoalStatus.Expired;
                    _goals.RemoveAt(i);
                    BumpVersion();
                    global::RimMind.Core.AgentBus.AgentBus.Publish(new GoalEvent(
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
                BumpVersion();
            }
        }

        public void Clear() => _goals.Clear();

        private void BumpVersion() => _version++;

        public void ExposeData()
        {
            var goals = _goals;
            Scribe_Collections.Look(ref goals, "goals", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _goals.Clear();
                if (goals != null) _goals.AddRange(goals);
            }
        }
    }
}
