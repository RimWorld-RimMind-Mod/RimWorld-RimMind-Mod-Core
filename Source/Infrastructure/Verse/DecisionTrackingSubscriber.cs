using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Domain.Events;

namespace RimMind.Infrastructure.Verse
{
    /// <summary>
    /// Subscribes to DecisionEvent to record agent decisions for flywheel calibration
    /// and trigger context cache invalidation when decisions change agent state.
    /// </summary>
    internal sealed class DecisionTrackingSubscriber : IDisposable
    {
        private readonly IAgentBus _eventBus;
        private readonly string _subscriptionKey;
        private readonly IFlywheelParameterStore _parameterStore;
        private readonly ILogSink _logSink;
        private int _disposed;

        public DecisionTrackingSubscriber(IAgentBus eventBus, IFlywheelParameterStore parameterStore, ILogSink logSink)
        {
            _eventBus = eventBus;
            _parameterStore = parameterStore;
            _logSink = logSink;
            _subscriptionKey = eventBus.Subscribe<DecisionEvent>(OnDecision);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _eventBus.Unsubscribe<DecisionEvent>(_subscriptionKey);
        }

        private void OnDecision(DecisionEvent e)
        {
            _logSink.Message($"[DecisionTracking] NpcId={e.NpcId}, Decision={e.DecisionType}, Reason={e.Reason}");
            // Record decision for flywheel calibration: tracks decision patterns
            // to adjust future context budgets and temperature parameters.
            _parameterStore.RecordAction(e.NpcId, e.DecisionType ?? "decision");
        }
    }
}
