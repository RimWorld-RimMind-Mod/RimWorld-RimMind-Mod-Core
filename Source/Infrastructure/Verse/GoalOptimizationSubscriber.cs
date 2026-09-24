using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.Events;

namespace RimMind.Infrastructure.Verse
{
    internal sealed class GoalOptimizationSubscriber : IDisposable
    {
        private readonly IAgentBus _eventBus;
        private readonly string _subscriptionKey;
        private readonly ILogSink _logSink;
        private int _disposed;

        public GoalOptimizationSubscriber(IAgentBus eventBus, ILogSink logSink)
        {
            _eventBus = eventBus;
            _logSink = logSink;
            _subscriptionKey = eventBus.Subscribe<GoalEvent>(OnGoal);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _eventBus.Unsubscribe<GoalEvent>(_subscriptionKey);
        }

        private void OnGoal(GoalEvent e)
        {
            _logSink.Message($"[GoalOptimization] Goal changed: NpcId={e.NpcId}, Status={e.Status}, Desc={e.GoalDescription}, Category={e.Category}");
            // Future: trigger StrategyOptimizer to re-evaluate goal priorities.
        }
    }
}
