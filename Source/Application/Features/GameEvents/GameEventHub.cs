using System;
using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.GameEvents
{
    public sealed record SkillLevelUpGameEvent(
        int PawnId,
        string SkillDefName,
        int OldLevel,
        int NewLevel);

    public sealed record HediffAddedGameEvent(
        int PawnId,
        string HediffDefName,
        string HediffLabel,
        float Severity,
        bool IsBad,
        bool Tendable,
        bool MakesSickThought,
        bool IsLifeThreatening,
        string? PartLabel);

    public sealed record PawnKilledGameEvent(
        int DeadPawnId,
        string DeadPawnName,
        bool WasColonist,
        IReadOnlyList<int> RelatedColonistIds);

    public sealed record IncidentExecutedGameEvent(
        string IncidentDefName,
        string? CategoryDefName,
        int TargetMapId);

    /// <summary>
    /// Shared pure significance rules for RimWorld game events consumed by multiple RimMind modules.
    /// </summary>
    public static class GameEventSignificancePolicy
    {
        public const float DefaultMinInjurySeverity = 0.20f;

        /// <summary>
        /// Determines whether an added Hediff represents a significant injury or illness worth
        /// recording in memory or triggering dialogue.
        /// </summary>
        public static bool IsSignificantInjury(
            bool isBad,
            bool tendable,
            bool makesSickThought,
            bool isLifeThreatening,
            float severity,
            float minSeverity = DefaultMinInjurySeverity)
        {
            if (!isBad && !tendable && !makesSickThought)
                return false;
            if (!isLifeThreatening && severity < minSeverity)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Thread-safe typed game-event bus with owner-scoped unsubscription and per-subscriber exception isolation.
    /// </summary>
    public sealed class GameEventHub
    {
        private sealed class SubscriptionEntry
        {
            public SubscriptionEntry(string ownerModId, Type eventType, Delegate handler)
            {
                OwnerModId = ownerModId;
                EventType = eventType;
                Handler = handler;
            }

            public string OwnerModId { get; }
            public Type EventType { get; }
            public Delegate Handler { get; }
        }

        private readonly object _sync = new object();
        private readonly List<SubscriptionEntry> _subscriptions = new List<SubscriptionEntry>();
        private readonly Action<string, Exception>? _onError;

        public GameEventHub(Action<string, Exception>? onError = null)
        {
            _onError = onError;
        }

        public static GameEventHub Shared { get; } = new GameEventHub();

        public int SubscriberCount
        {
            get
            {
                lock (_sync)
                {
                    return _subscriptions.Count;
                }
            }
        }

        public void Subscribe<TEvent>(string ownerModId, Action<TEvent> handler)
        {
            if (string.IsNullOrWhiteSpace(ownerModId))
                throw new ArgumentException("Owner mod ID cannot be null or whitespace.", nameof(ownerModId));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_sync)
            {
                _subscriptions.Add(new SubscriptionEntry(ownerModId, typeof(TEvent), handler));
            }
        }

        public int UnsubscribeByOwner(string ownerModId)
        {
            if (string.IsNullOrWhiteSpace(ownerModId))
                return 0;

            lock (_sync)
            {
                return _subscriptions.RemoveAll(s =>
                    string.Equals(s.OwnerModId, ownerModId, StringComparison.Ordinal));
            }
        }

        public void Publish<TEvent>(TEvent evt)
        {
            if (evt == null) return;

            List<SubscriptionEntry>? matching = null;
            Type targetType = typeof(TEvent);
            lock (_sync)
            {
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    var sub = _subscriptions[i];
                    if (sub.EventType == targetType)
                    {
                        matching ??= new List<SubscriptionEntry>();
                        matching.Add(sub);
                    }
                }
            }

            if (matching == null) return;

            for (int i = 0; i < matching.Count; i++)
            {
                var sub = matching[i];
                try
                {
                    ((Action<TEvent>)sub.Handler)(evt);
                }
                catch (Exception ex)
                {
                    if (_onError != null)
                    {
                        try { _onError(sub.OwnerModId, ex); } catch { }
                    }
                    else
                    {
                        RimMindErrors.Warn($"[RimMind-Core] GameEventHub subscriber '{sub.OwnerModId}' threw on {targetType.Name}: {ex.Message}");
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _subscriptions.Clear();
            }
        }
    }
}
