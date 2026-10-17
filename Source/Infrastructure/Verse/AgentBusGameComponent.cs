using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Defaults;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.Runtime.Services;

using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class AgentBusGameComponent : GameComponent
    {
        private readonly RuntimeBinding _binding = new RuntimeBinding();
        private AgentBusQueueTickCoordinator? _tickCoordinator;

        public AgentBusGameComponent(Game game) : base() { }

        private void Refresh()
        {
            _binding.Refresh(Bind);
        }

        public override void StartedNewGame()
        {
            Refresh();
        }

        public override void LoadedGame()
        {
            Refresh();
        }

        private int _pausedVirtualTicks;
        private float _pausedTickAccumulator;

        public override void GameComponentTick()
        {
            Refresh();
            _pausedVirtualTicks = 0;
            _pausedTickAccumulator = 0f;
            _tickCoordinator?.Tick(Find.TickManager.TicksGame);
        }

        public override void GameComponentUpdate()
        {
            if (Find.TickManager == null || !Find.TickManager.Paused)
                return;

            Refresh();
            _pausedTickAccumulator += UnityEngine.Time.unscaledDeltaTime * 60f;
            int stepTicks = (int)_pausedTickAccumulator;
            if (stepTicks > 0)
            {
                _pausedTickAccumulator -= stepTicks;
                _pausedVirtualTicks += stepTicks;
                _tickCoordinator?.Tick(Find.TickManager.TicksGame + _pausedVirtualTicks);
            }
        }

        private IDisposable? Bind(RuntimeServiceScope scope)
        {
            var agentBus = scope.GetOptional<IAgentBus>();
            var requestQueue = scope.GetOptional<ITickableRequestQueue>();
            var logSink = scope.GetOptional<ILogSink>();
            var cacheManager = scope.GetOptional<IContextCacheManager>();
            var parameterStore = scope.GetOptional<IFlywheelParameterStore>();
            _tickCoordinator = agentBus != null && requestQueue != null
                ? new AgentBusQueueTickCoordinator(agentBus, requestQueue)
                : null;

            if (requestQueue != null)
                AIRequestQueueGameComponent.Configure(requestQueue);

            if (agentBus == null || logSink == null)
                return null;

            var subscribers = new List<IDisposable>
            {
                new AgentBusCoreSubscriber(agentBus, logSink),
                new GoalOptimizationSubscriber(agentBus, logSink)
            };
            if (cacheManager != null)
            {
                subscribers.Add(new ContextInvalidationSubscriber(agentBus, cacheManager, logSink));
                subscribers.Add(new NpcCleanupSubscriber(agentBus, cacheManager, logSink));
            }
            if (parameterStore != null)
            {
                subscribers.Add(new FlywheelCalibrationSubscriber(agentBus, parameterStore, logSink));
                subscribers.Add(new DecisionTrackingSubscriber(agentBus, parameterStore, logSink));
            }

            return new SubscriberLease(subscribers);
        }

        public void Dispose()
        {
            _binding.Dispose();
            _tickCoordinator = null;
        }

        private sealed class SubscriberLease : IDisposable
        {
            private readonly List<IDisposable> _subscribers;
            private bool _disposed;

            public SubscriberLease(List<IDisposable> subscribers)
            {
                _subscribers = subscribers;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                for (var index = _subscribers.Count - 1; index >= 0; index--)
                    _subscribers[index].Dispose();

                _subscribers.Clear();
            }
        }
    }
}
