using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;

namespace RimMind.Presentation.Runtime
{
    /// <summary>
    /// Lifecycle Manager: responsible for Initialize, Shutdown, and Reset operations.
    /// Extracted from RimMindRuntime to satisfy SRP.
    /// </summary>
    internal sealed class RimMindLifecycleManager
    {
        private readonly ITelemetryCollector _telemetry;
        private readonly IContextEngine _contextEngine;
        private readonly IPlayer2Lifecycle? _player2Lifecycle;
        private readonly IAgentBus _agentBus;
        private readonly IContextKeyRegistry? _keyRegistry;
        private volatile bool _isShutdown;

        public bool IsShutdown => _isShutdown;

        public RimMindLifecycleManager(
            ITelemetryCollector telemetry,
            IContextEngine contextEngine,
            IPlayer2Lifecycle? player2Lifecycle,
            IAgentBus agentBus,
            IContextKeyRegistry? keyRegistry = null)
        {
            _telemetry = telemetry;
            _contextEngine = contextEngine;
            _player2Lifecycle = player2Lifecycle;
            _agentBus = agentBus;
            _keyRegistry = keyRegistry;
        }

        public void Shutdown()
        {
            if (_isShutdown) return;
            _isShutdown = true;
            (_telemetry as IDisposable)?.Dispose();
            _contextEngine.Dispose();
            _keyRegistry?.Clear();
            _agentBus.ClearAllSubscribers();
            _player2Lifecycle?.StopHealthCheck();
        }

    }
}
