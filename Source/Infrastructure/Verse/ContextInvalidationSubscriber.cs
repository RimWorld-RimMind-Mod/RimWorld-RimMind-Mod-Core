using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.Events;

namespace RimMind.Infrastructure.Verse
{
    internal sealed class ContextInvalidationSubscriber : IDisposable
    {
        private readonly IAgentBus _eventBus;
        private readonly string _subscriptionKey;
        private readonly IContextCacheManager _cacheManager;
        private readonly ILogSink _logSink;
        private int _disposed;

        public ContextInvalidationSubscriber(IAgentBus eventBus, IContextCacheManager cacheManager, ILogSink logSink)
        {
            _eventBus = eventBus;
            _cacheManager = cacheManager;
            _logSink = logSink;
            _subscriptionKey = eventBus.Subscribe<PerceptionEvent>(OnPerception);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _eventBus.Unsubscribe<PerceptionEvent>(_subscriptionKey);
        }

        private void OnPerception(PerceptionEvent e)
        {
            _cacheManager.InvalidateNpc(e.NpcId);
            _logSink.Message($"[ContextInvalidation] Invalidated cache for NpcId={e.NpcId} on PerceptionType={e.PerceptionType}");
        }
    }
}
