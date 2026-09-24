using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.Events;

namespace RimMind.Infrastructure.Verse
{
    internal sealed class NpcCleanupSubscriber : IDisposable
    {
        private readonly IAgentBus _eventBus;
        private readonly string _subscriptionKey;
        private readonly IContextCacheManager _cacheManager;
        private readonly ILogSink _logSink;
        private int _disposed;

        public NpcCleanupSubscriber(IAgentBus eventBus, IContextCacheManager cacheManager, ILogSink logSink)
        {
            _eventBus = eventBus;
            _cacheManager = cacheManager;
            _logSink = logSink;
            _subscriptionKey = eventBus.Subscribe<AgentLifecycleEvent>(OnLifecycle);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _eventBus.Unsubscribe<AgentLifecycleEvent>(_subscriptionKey);
        }

        private void OnLifecycle(AgentLifecycleEvent e)
        {
            if (e.NewState == "Dead")
            {
                _cacheManager.InvalidateNpc(e.NpcId);
                _logSink.Message($"[NpcCleanup] Cleaned up cache for dead NPC: NpcId={e.NpcId}");
            }
        }
    }
}
