using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.UI;
using Verse;

namespace RimMind.Presentation.Runtime
{
    public class OverlayService : IOverlayService
    {
        private readonly List<RequestEntry> _pendingRequests = new List<RequestEntry>();
        private const int MaxEntries = 50;

        public void RegisterPendingRequest(RequestEntry entry)
        {
            if (entry == null) return;

            List<RequestEntry>? evicted = null;
            lock (_pendingRequests)
            {
                foreach (var pending in _pendingRequests)
                {
                    if (ReferenceEquals(pending, entry))
                        return;
                }

                var now = Find.TickManager?.TicksGame ?? 0;
                entry.tick = now;
                if (entry.ExpireAtTicks <= 0 && entry.expireTicks > 0)
                {
                    var expireAt = (long)now + entry.expireTicks;
                    entry.ExpireAtTicks = expireAt > int.MaxValue ? int.MaxValue : (int)expireAt;
                }

                _pendingRequests.Add(entry);
                while (_pendingRequests.Count > MaxEntries)
                {
                    evicted ??= new List<RequestEntry>();
                    evicted.Add(_pendingRequests[0]);
                    _pendingRequests.RemoveAt(0);
                }
            }

            CompleteWithFallback(evicted, RequestCompletionReason.Evicted);
        }

        public IReadOnlyList<RequestEntry> GetPendingRequests()
        {
            lock (_pendingRequests)
                return _pendingRequests.ToArray();
        }

        public bool TryResolve(RequestEntry entry, string choice)
        {
            if (!TryRemove(entry)) return false;
            return CompleteSafely(entry, choice, RequestCompletionReason.Selected);
        }

        public bool TryDismiss(RequestEntry entry)
        {
            if (!TryRemove(entry)) return false;
            return CompleteSafely(entry, null, RequestCompletionReason.Dismissed);
        }

        public void Clear()
        {
            List<RequestEntry> dismissed;
            lock (_pendingRequests)
            {
                dismissed = new List<RequestEntry>(_pendingRequests);
                _pendingRequests.Clear();
            }

            foreach (var entry in dismissed)
                CompleteSafely(entry, null, RequestCompletionReason.Dismissed);
        }

        public void Tick()
        {
            int now = Find.TickManager.TicksGame;
            List<RequestEntry>? expired = null;
            lock (_pendingRequests)
            {
                for (var i = _pendingRequests.Count - 1; i >= 0; i--)
                {
                    var entry = _pendingRequests[i];
                    if (entry.ExpireAtTicks <= 0 || now < entry.ExpireAtTicks) continue;

                    expired ??= new List<RequestEntry>();
                    expired.Add(entry);
                    _pendingRequests.RemoveAt(i);
                }
            }

            CompleteWithFallback(expired, RequestCompletionReason.Expired);
        }

        private bool TryRemove(RequestEntry entry)
        {
            lock (_pendingRequests)
                return _pendingRequests.Remove(entry);
        }

        private static void CompleteWithFallback(
            List<RequestEntry>? entries,
            RequestCompletionReason completionReason)
        {
            if (entries == null) return;

            foreach (var entry in entries)
            {
                var fallbackChoice = entry.completionCallback == null && entry.options.Length > 0
                    ? entry.options[entry.options.Length - 1]
                    : null;
                CompleteSafely(entry, fallbackChoice, completionReason);
            }
        }

        private static bool CompleteSafely(
            RequestEntry entry,
            string? choice,
            RequestCompletionReason completionReason)
        {
            try
            {
                return entry.TryComplete(choice, completionReason);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[RimMind-Core] Pending request completion failed " +
                    $"(source={entry.source}, reason={completionReason}): {ex}");
                return true;
            }
        }
    }
}
