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
        private ILogSink? _log;
        private readonly IThreadChecker? _threadChecker;
        private readonly Action<AgentBusEvent> _dispatchAction;
        private IPipeline<BusPublishContext>? _pipeline;
        private int _handlerIdCounter;

        /// <summary>
        /// Name-based subscriptions whose event name is not (yet) registered. They stay inert —
        /// never bound to the base <see cref="AgentBusEvent"/> type — and are bound as soon as a
        /// matching <see cref="RegisterEventType"/> call makes the name resolvable.
        /// </summary>
        private readonly ConcurrentDictionary<string, PendingNamedSubscription> _pendingByKey
            = new ConcurrentDictionary<string, PendingNamedSubscription>(StringComparer.Ordinal);
        private int _pendingCount;
        private readonly ConcurrentDictionary<string, byte> _warnedUnknownNames
            = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

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
            _dispatchAction = DispatchToHandlers;
        }

        public void SetLogSink(ILogSink? log)
        {
            _log = log;
        }

        public void SetPipeline(IPipeline<BusPublishContext> pipeline)
        {
            _pipeline = pipeline;
        }

        public Action<AgentBusEvent>? DispatchAction => _dispatchAction;

        internal void DispatchToHandlers(AgentBusEvent evt)
        {
            if (evt == null) return;
            BindPendingSubscriptions();
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
            AddHandler(typeof(T), entry);
        }

        private void AddHandler(Type eventType, HandlerEntry entry)
        {
            _handlers.AddOrUpdate(
                eventType,
                _ => new List<HandlerEntry> { entry },
                (_, list) => { lock (list) { list.Add(entry); } return list; });
        }

        /// <summary>
        /// Subscribes by registered event name. Names resolve only through the event type registry
        /// (built-in <see cref="AgentBusEventType"/> names plus <see cref="RegisterEventType"/>).
        /// An unknown name never falls back to the base <see cref="AgentBusEvent"/>: the returned
        /// subscription stays inert (a warning is logged once per name) and is bound automatically
        /// when the name is registered later, so module init order does not matter.
        /// </summary>
        public string SubscribeByName(string eventTypeName, Action<AgentBusEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var key = $"byname_{Interlocked.Increment(ref _handlerIdCounter)}";
            if (string.IsNullOrWhiteSpace(eventTypeName))
            {
                _log?.Warning("[AgentBus] SubscribeByName: empty event type name ignored; subscription is inert.");
                return key;
            }

            if (!EventTypeMap.TryGetValue(eventTypeName, out var eventType))
            {
                if (_pendingByKey.TryAdd(key, new PendingNamedSubscription(eventTypeName, handler)))
                    Interlocked.Increment(ref _pendingCount);
                if (_warnedUnknownNames.TryAdd(eventTypeName, 0))
                {
                    _log?.Warning($"[AgentBus] SubscribeByName: no event type registered for '{eventTypeName}'. " +
                        "The subscription stays inert (it will not receive base AgentBusEvent) until " +
                        $"RegisterEventType(\"{eventTypeName}\", type) is called.");
                }
                // Registration may have raced with this subscribe; bind immediately if so.
                BindPendingSubscriptions();
                return key;
            }

            AddHandler(eventType, new HandlerEntry(key, h => handler((AgentBusEvent)h)));
            return key;
        }

        /// <summary>
        /// Moves inert name-based subscriptions whose names have become resolvable onto their
        /// registered event types. Cheap no-op when nothing is pending.
        /// </summary>
        private void BindPendingSubscriptions()
        {
            if (Volatile.Read(ref _pendingCount) == 0) return;
            foreach (var kvp in _pendingByKey)
            {
                if (!EventTypeMap.TryGetValue(kvp.Value.EventTypeName, out var eventType)) continue;
                if (!_pendingByKey.TryRemove(kvp.Key, out var pending)) continue;
                Interlocked.Decrement(ref _pendingCount);
                var handler = pending.Handler;
                AddHandler(eventType, new HandlerEntry(kvp.Key, h => handler((AgentBusEvent)h)));
            }
        }

        private void RemovePending(string key)
        {
            if (key != null && _pendingByKey.TryRemove(key, out _))
                Interlocked.Decrement(ref _pendingCount);
        }

        public void Unsubscribe<T>(string key) where T : AgentBusEvent
        {
            RemovePending(key);
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
            RemovePending(key);
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
            BindPendingSubscriptions();
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
            BindPendingSubscriptions();
            if (!_handlers.TryGetValue(evt.GetType(), out var list) || list.Count == 0) return;
            _backgroundQueue.Enqueue(new DeferredPublish(evt.GetType(), evt));
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
            _pendingByKey.Clear();
            Interlocked.Exchange(ref _pendingCount, 0);
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
            RegisterKnownEventType(name, eventType);
            _log?.Message($"[RimMind.AgentBus] action=RegisterEventType name={name} type={eventType.Name}");
            BindPendingSubscriptions();
        }

        /// <summary>
        /// Adds a name → event type mapping to the process-wide registry without requiring a live bus
        /// instance. Bus instances bind their inert name-based subscriptions to it on the next publish.
        /// </summary>
        public static void RegisterKnownEventType(string name, Type eventType)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (!typeof(AgentBusEvent).IsAssignableFrom(eventType))
                throw new ArgumentException($"Event type must inherit from AgentBusEvent, got {eventType.FullName}", nameof(eventType));

            // AddOrUpdate preserves the original Dictionary overwrite semantics:
            // a duplicate RegisterEventType call replaces the previously registered Type.
            EventTypeMap.AddOrUpdate(name, eventType, (_, __) => eventType);
        }

        /// <summary>Returns true when <paramref name="name"/> resolves to a registered event type.</summary>
        public static bool IsEventTypeRegistered(string name)
            => !string.IsNullOrWhiteSpace(name) && EventTypeMap.ContainsKey(name);

        private sealed class PendingNamedSubscription
        {
            public readonly string EventTypeName;
            public readonly Action<AgentBusEvent> Handler;
            public PendingNamedSubscription(string eventTypeName, Action<AgentBusEvent> handler)
            {
                EventTypeName = eventTypeName;
                Handler = handler;
            }
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
