using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Events;
using Verse;

namespace RimMind.Presentation.Agent
{
    public class PawnRecorder : IPawnRecorder
    {
        private readonly IPawnAgentVerse _agent;
        private readonly IAgentBus _agentBus;
        private readonly List<BehaviorRecord> _history = new List<BehaviorRecord>();
        private const int MaxHistory = RimMindDefaults.BehaviorHistoryMax;

        public PawnRecorder(IPawnAgentVerse agent, IAgentBus agentBus)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
        }

        public IReadOnlyList<BehaviorRecord> History => _history;

        public void Record(BehaviorRecord record)
        {
            if (record == null) return;
            _history.Add(record);
            while (_history.Count > MaxHistory)
                _history.RemoveAt(0);
            _agentBus.Publish(new ActionEvent(
                _agent.Identity.NpcId, _agent.Pawn?.thingIDNumber ?? 0,
                record.Action, record.Success, record.Reason, Guid.NewGuid().ToString()));
        }

        public void RecordAction(string action, string reason, bool success, float goalProgressDelta = 0f)
        {
            Record(new BehaviorRecord
            {
                Action = action,
                Reason = reason,
                Success = success,
                GoalProgressDelta = goalProgressDelta,
                Timestamp = Find.TickManager.TicksGame
            });
        }

        public void Clear()
        {
            _history.Clear();
        }

        /// <summary>
        /// Get the most recent N behavior records.
        /// </summary>
        public IReadOnlyList<BehaviorRecord> GetRecentHistory(int count = 10)
        {
            if (count <= 0 || _history.Count == 0) return Array.Empty<BehaviorRecord>();
            var skip = Math.Max(0, _history.Count - count);
            return _history.Skip(skip).ToList();
        }

        /// <summary>
        /// Get the success rate of the most recent N behavior records.
        /// Returns 0.0 if no records exist.
        /// </summary>
        public float GetRecentSuccessRate(int count = 10)
        {
            var recent = GetRecentHistory(count);
            if (recent.Count == 0) return 0f;
            return (float)Enumerable.Count(recent, r => r.Success) / recent.Count;
        }
    }
}
