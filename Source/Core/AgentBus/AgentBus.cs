using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace RimMind.Core.AgentBus
{
    public static class AgentBus
    {
        private static readonly int _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        private static readonly ConcurrentDictionary<Type, List<Delegate>> _handlers
            = new ConcurrentDictionary<Type, List<Delegate>>();

        private static readonly ConcurrentQueue<AgentBusEvent> _backgroundQueue
            = new ConcurrentQueue<AgentBusEvent>();

        private static readonly List<AgentBusEvent> _pendingDispatch
            = new List<AgentBusEvent>();

        public static void Subscribe<T>(Action<T> handler) where T : AgentBusEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            var list = _handlers.GetOrAdd(type, _ => new List<Delegate>());
            lock (list)
            {
                list.Add(handler);
            }
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : AgentBusEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var list)) return;
            lock (list)
            {
                list.Remove(handler);
            }
        }

        public static void Publish<T>(T evt) where T : AgentBusEvent
        {
            if (evt == null) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                Log.Warning($"[RimMind] AgentBus.Publish called from background thread for {typeof(T).Name}, use PublishFromBackground instead");
                PublishFromBackground(evt);
                return;
            }
            evt.Timestamp = Verse.Find.TickManager?.TicksGame ?? 0;
            DispatchToHandlers(evt);
        }

        public static void PublishFromBackground<T>(T evt) where T : AgentBusEvent
        {
            if (evt == null) return;
            _backgroundQueue.Enqueue(evt);
        }

        public static void FlushBackgroundQueue()
        {
            _pendingDispatch.Clear();
            while (_backgroundQueue.TryDequeue(out var evt))
                _pendingDispatch.Add(evt);

            for (int i = 0; i < _pendingDispatch.Count; i++)
            {
                _pendingDispatch[i].Timestamp = Verse.Find.TickManager?.TicksGame ?? 0;
                DispatchToHandlers(_pendingDispatch[i]);
            }

            _pendingDispatch.Clear();
        }

        private static void DispatchToHandlers<T>(T evt) where T : AgentBusEvent
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var list)) return;

            Delegate[] snapshot;
            lock (list)
            {
                snapshot = list.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    if (snapshot[i] is Action<T> action)
                        action(evt);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] AgentBus handler error for {type.Name}: {ex.Message}");
                }
            }
        }
    }
}
