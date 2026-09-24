using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Common;
using RimMind.Domain.Events;

namespace RimMind.Application.Features.AgentBus
{
    public sealed class AgentBusImpl : IAgentBus
    {
        public event Action? SubscribersCleared;

        private readonly ConcurrentDictionary<Type, List<HandlerEntry>> _handlers
            = new ConcurrentDictionary<Type, List<HandlerEntry>>();
        private readonly ConcurrentQueue<DeferredPublish> _backgroundQueue
            = new ConcurrentQueue<DeferredPublish>();
        private readonly ILogSink? _log;
        private readonly IThreadChecker? _threadChecker;
        private IPipeline<BusPublishContext>? _pipeline;
        private int _handlerIdCounter;

        /// <summary>
        /// Maps AgentBusEventType enum names to concrete event Types for SubscribeByName resolution.
        /// Mutable to support RegisterEventType for custom event types from sub-mods.
        /// Thread-safe: RegisterEventType (write) and SubscribeByName (read) may be invoked
        /// concurrently from sub-mod init and game threads, so ConcurrentDictionary is required
        /// to avoid InvalidOperationException ("Collection was modified") and silent data corruption.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Type> EventTypeMap = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            // ConcurrentDictionary has no public Add(TKey,TValue) method, so a collection
            // initializer { key, value } would fail to compile. Use the indexer initializer
            // syntax (C# 6+) which invokes the public Item set (add-or-update semantics).
            [nameof(AgentBusEventType.Perception)] = typeof(PerceptionEvent),
            [nameof(AgentBusEventType.Decision)] = typeof(DecisionEvent),
            [nameof(AgentBusEventType.Goal)] = typeof(GoalEvent),
            [nameof(AgentBusEventType.Action)] = typeof(ActionEvent),
            [nameof(AgentBusEventType.Lifecycle)] = typeof(AgentLifecycleEvent),
            [nameof(AgentBusEventType.ModeChange)] = typeof(AgentModeChangedEvent),
            [nameof(AgentBusEventType.InnerVoice)] = typeof(InnerVoiceEvent),
            [nameof(AgentBusEventType.Reflection)] = typeof(DecisionEvent),
            [nameof(AgentBusEventType.ScheduleUpdate)] = typeof(DecisionEvent),
            [nameof(AgentBusEventType.MoodThreshold)] = typeof(MoodThresholdCrossedEvent),
            [nameof(AgentBusEventType.NeedCritical)] = typeof(NeedCriticalEvent),
            [nameof(AgentBusEventType.MentalStateWarning)] = typeof(MentalStateWarningEvent),
            [nameof(AgentBusEventType.InformationDiffusion)] = typeof(InformationDiffusionEvent),
            [nameof(AgentBusEventType.SocialEventProposed)] = typeof(SocialEventProposedEvent),
            [nameof(AgentBusEventType.TraitEvolution)] = typeof(TraitEvolutionEvent),
            [nameof(AgentBusEventType.Dream)] = typeof(DreamEvent),
            [nameof(AgentBusEventType.DecisionFailed)] = typeof(DecisionFailedEvent),
            [nameof(AgentBusEventType.WorkflowPhaseChange)] = typeof(AgentBusEvent),
        };

        public AgentBusImpl(ILogSink? log = null, IThreadChecker? threadChecker = null)
        {
            _log = log;
            _threadChecker = threadChecker;
        }

        public void SetPipeline(IPipeline<BusPublishContext> pipeline)
        {
            _pipeline = pipeline;
        }

        public Action<AgentBusEvent>? DispatchAction => DispatchToHandlers;

        internal void DispatchToHandlers(AgentBusEvent evt)
        {
            if (evt == null) return;
            if (!_handlers.TryGetValue(evt.GetType(), out var list) || list.Count == 0) return;
            HandlerEntry[] snapshot;
            lock (list) { snapshot = list.ToArray(); }
            foreach (var entry in snapshot)
            {
                try { entry.Action(evt); }
                catch (Exception ex)
                {
                    var errorMsg = $"AgentBus handler error: {ex}";
                    if (_log != null)
                        _log.Error(errorMsg);
                    else
                        System.Diagnostics.Debug.WriteLine(errorMsg);
                }
            }
        }

        public string Subscribe<T>(Action<T> handler) where T : AgentBusEvent
        {
            var key = $"auto_{Interlocked.Increment(ref _handlerIdCounter)}";
            Subscribe(key, handler);
            return key;
        }

        public void Subscribe<T>(string key, Action<T> handler) where T : AgentBusEvent
        {
            var entry = new HandlerEntry(key, h => handler((T)h));
            _handlers.AddOrUpdate(
                typeof(T),
                _ => new List<HandlerEntry> { entry },
                (_, list) => { lock (list) { list.Add(entry); } return list; });
        }

        public string SubscribeByName(string eventTypeName, Action<AgentBusEvent> handler)
        {
            if (!EventTypeMap.TryGetValue(eventTypeName, out var eventType))
            {
                _log?.Warning($"[AgentBus] SubscribeByName: unknown event type '{eventTypeName}', falling back to base AgentBusEvent type.");
                eventType = typeof(AgentBusEvent);
            }

            var key = $"byname_{Interlocked.Increment(ref _handlerIdCounter)}";
            var entry = new HandlerEntry(key, h => handler((AgentBusEvent)h));
            _handlers.AddOrUpdate(
                eventType,
                _ => new List<HandlerEntry> { entry },
                (_, list) => { lock (list) { list.Add(entry); } return list; });
            return key;
        }

        public void Unsubscribe<T>(string key) where T : AgentBusEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                lock (list)
                {
                    list.RemoveAll(e => e.Key == key);
                }
            }
        }

        public void Unsubscribe(string key)
        {
            foreach (var handlers in _handlers.Values)
            {
                lock (handlers)
                {
                    handlers.RemoveAll(entry => entry.Key == key);
                }
            }
        }

        /// <summary>
        /// Obsolete: action-based unsubscribe is unreliable because subscriptions wrap the original handler in a lambda.
        /// Use <see cref="Unsubscribe{T}(string)"/> with the key returned from <see cref="Subscribe{T}(Action{T})"/>.
        /// </summary>
        [Obsolete("Use Unsubscribe<T>(string key) instead. Action-based unsubscribe is unreliable due to lambda wrapping.")]
        public void Unsubscribe<T>(Action<T> handler) where T : AgentBusEvent
        {
            throw new NotSupportedException(
                "Unsubscribe by Action is unreliable due to lambda wrapping. " +
                "Use Unsubscribe<T>(string key) with the key returned from Subscribe<T>(Action<T>).");
        }

        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        public void Publish<T>(T evt) where T : AgentBusEvent
        {
            if (evt == null) return;
            if (_pipeline != null)
            {
                var context = new BusPublishContext(evt);
                _pipeline.ExecuteAsync(context).GetAwaiter().GetResult();
                return;
            }
            DispatchToHandlers(evt);
        }

        [ThreadAffinity(ThreadAffinityKind.Any)]
        public void PublishFromBackground<T>(T evt) where T : AgentBusEvent
        {
            if (evt == null) return;
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            _backgroundQueue.Enqueue(new DeferredPublish(typeof(T), evt));
        }

        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        public void FlushBackgroundQueue()
        {
            while (_backgroundQueue.TryDequeue(out var deferred))
            {
                if (deferred.Event is AgentBusEvent evt)
                {
                    if (_pipeline != null)
                    {
                        var context = new BusPublishContext(evt);
                        _pipeline.ExecuteAsync(context).GetAwaiter().GetResult();
                    }
                    else
                    {
                        DispatchToHandlers(evt);
                    }
                }
            }
        }

        public void ClearAllSubscribers()
        {
            _handlers.Clear();
            SubscribersCleared?.Invoke();
        }

        public int GetHandlerCount()
        {
            int count = 0;
            foreach (var kvp in _handlers)
            {
                lock (kvp.Value) { count += kvp.Value.Count; }
            }
            return count;
        }

        public int GetBackgroundQueueCount() => _backgroundQueue.Count;

        public void RegisterEventType(string name, Type eventType)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (!typeof(AgentBusEvent).IsAssignableFrom(eventType))
                throw new ArgumentException($"Event type must inherit from AgentBusEvent, got {eventType.FullName}", nameof(eventType));

            // AddOrUpdate preserves the original Dictionary overwrite semantics:
            // a duplicate RegisterEventType call replaces the previously registered Type.
            EventTypeMap.AddOrUpdate(name, eventType, (_, __) => eventType);
            _log?.Message($"[RimMind.AgentBus] action=RegisterEventType name={name} type={eventType.Name}");
        }

        private sealed class HandlerEntry
        {
            public string Key;
            public Action<object> Action;
            public HandlerEntry(string key, Action<object> action) { Key = key; Action = action; }
        }

        private sealed class DeferredPublish
        {
            public Type EventType;
            public object Event;
            public DeferredPublish(Type type, object evt) { EventType = type; Event = evt; }
        }
    }
}
