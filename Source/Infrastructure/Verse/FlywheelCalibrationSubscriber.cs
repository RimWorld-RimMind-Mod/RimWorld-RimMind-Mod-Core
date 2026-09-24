using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Domain.Events;

namespace RimMind.Infrastructure.Verse
{
    internal sealed class FlywheelCalibrationSubscriber : IDisposable
    {
        private readonly IAgentBus _eventBus;
        private readonly string _subscriptionKey;
        private readonly IFlywheelParameterStore _parameterStore;
        private readonly ILogSink _logSink;
        private int _disposed;

        public FlywheelCalibrationSubscriber(IAgentBus eventBus, IFlywheelParameterStore parameterStore, ILogSink logSink)
        {
            _eventBus = eventBus;
            _parameterStore = parameterStore;
            _logSink = logSink;
            _subscriptionKey = eventBus.Subscribe<ActionEvent>(OnAction);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _eventBus.Unsubscribe<ActionEvent>(_subscriptionKey);
        }

        private void OnAction(ActionEvent e)
        {
            _parameterStore.RecordAction(e.NpcId, e.ActionName);
            _logSink.Message($"[FlywheelCalibration] Recorded action for NpcId={e.NpcId}, Action={e.ActionName}, Success={e.Success}");
        }
    }
}
