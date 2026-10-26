using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static class QueueTabDrawer
    {
        internal const int MaxDisplayedRequests = 50;

        private static Vector2 _queueScroll;
        private static float _lastMeasuredHeight;
        private static readonly List<string> SortedModIds = new();
        internal static Vector2 ScrollPosition { get => _queueScroll; set => _queueScroll = value; }

        private static readonly RuntimeServiceRef<IRequestQueue> RequestQueue =
            RuntimeServiceRef<IRequestQueue>.Optional();

        public static void Draw(
            Rect inRect,
            ISettingsProvider settings,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            var queue = RequestQueue.ResolveOptional(runtimeScope);
            if (queue == null)
            {
                var listing0 = new Listing_Standard();
                listing0.Begin(inRect);
                GUI.color = Color.yellow;
                listing0.Label(UiText.Get("RimMind.Settings.QueueNotAvailable", "Queue not initialized"));
                GUI.color = Color.white;
                listing0.End();
                return;
            }

            var allDepths = queue.GetAllQueueDepths();
            var allCooldowns = queue.GetAllCooldowns();
            CollectSortedModIds(allDepths, allCooldowns, SortedModIds);

            int modCount = SortedModIds.Count;
            int activeCount = Mathf.Min(queue.ActiveRequestCount, MaxDisplayedRequests + 1);
            int queuedCount = Mathf.Min(queue.TotalQueuedCount, MaxDisplayedRequests + 1);
            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 5, rowsPerSection: 4);
            float contentH = 60f + 28f + modCount * 26f + 28f + activeCount * 24f + 28f + queuedCount * 24f + 80f;
            contentH = Mathf.Max(contentH, Mathf.Max(_lastMeasuredHeight, formLayout.ContentHeight));
            contentH = Mathf.Max(contentH, inRect.height + 10f);

            Rect viewRect = new Rect(0f, 0f, formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth, contentH);
            Widgets.BeginScrollView(inRect, ref _queueScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Queue:Viewport");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawQueueStatus(listing, queue, settings);
            DrawQueueControls(listing, queue);
            DrawPerModCooldowns(listing, queue, allDepths, allCooldowns, SortedModIds);
            DrawActiveRequests(listing, queue);
            DrawQueuedRequests(listing, queue);

            _lastMeasuredHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void CollectSortedModIds(
            IReadOnlyDictionary<string, int> allDepths,
            IReadOnlyDictionary<string, int> allCooldowns,
            List<string> destination)
        {
            destination.Clear();
            foreach (var key in allDepths.Keys)
            {
                if (!string.IsNullOrEmpty(key) && !destination.Contains(key))
                    destination.Add(key);
            }
            foreach (var key in allCooldowns.Keys)
            {
                if (!string.IsNullOrEmpty(key) && !destination.Contains(key))
                    destination.Add(key);
            }
            destination.Sort(StringComparer.Ordinal);
        }

        private static void DrawQueueStatus(Listing_Standard listing, IRequestQueue queue, ISettingsProvider settings)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Queue.Status"));

            string pauseLabel = queue.IsPaused
                ? UiText.Get("RimMind.Settings.QueuePaused")
                : UiText.Get("RimMind.Settings.QueueRunning");
            GUI.color = queue.IsPaused ? Color.yellow : new Color(0.4f, 0.9f, 0.4f);
            listing.Label(pauseLabel);
            GUI.color = Color.white;

            listing.Label($"{UiText.Get("RimMind.Settings.Queue.Active")}: {queue.ActiveRequestCount} / {settings.MaxConcurrentRequests}");
            listing.Label($"{UiText.Get("RimMind.Settings.Queue.Queued")}: {queue.TotalQueuedCount}");
            GUI.color = queue.IsLocalModelBusy ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
            listing.Label($"{UiText.Get("RimMind.Settings.Queue.LocalModel")}: {(queue.IsLocalModelBusy ? UiText.Get("RimMind.Settings.Queue.Busy") : UiText.Get("RimMind.Settings.Queue.Idle"))}");
            GUI.color = Color.white;
        }

        private static void DrawQueueControls(Listing_Standard listing, IRequestQueue queue)
        {
            listing.Gap(4f);
            Rect btnRow = listing.GetRect(28f);
            const float btnW = 110f;
            const float gap = 8f;

            Rect pauseBtn = new Rect(btnRow.x, btnRow.y, btnW, btnRow.height);
            Rect clearBtn = new Rect(pauseBtn.xMax + gap, btnRow.y, btnW, btnRow.height);
            Rect clearCdBtn = new Rect(clearBtn.xMax + gap, btnRow.y, btnW + 20f, btnRow.height);

            string pauseText = queue.IsPaused
                ? UiText.Get("RimMind.Settings.Queue.Resume")
                : UiText.Get("RimMind.Settings.Queue.Pause");
            if (Widgets.ButtonText(pauseBtn, pauseText))
            {
                if (queue.IsPaused) queue.ResumeQueue();
                else queue.PauseQueue();
            }
            TooltipHandler.TipRegion(pauseBtn, pauseText);

            string clearQueuesLabel = UiText.Get("RimMind.Settings.Queue.ClearQueues");
            if (Widgets.ButtonText(clearBtn, clearQueuesLabel))
                queue.ClearAllQueues();
            TooltipHandler.TipRegion(clearBtn, clearQueuesLabel);

            string clearCooldownsLabel = UiText.Get("RimMind.Settings.Queue.ClearCooldowns");
            if (Widgets.ButtonText(clearCdBtn, clearCooldownsLabel))
                queue.ClearAllCooldowns();
            TooltipHandler.TipRegion(clearCdBtn, clearCooldownsLabel);
        }

        private static void DrawPerModCooldowns(
            Listing_Standard listing,
            IRequestQueue queue,
            IReadOnlyDictionary<string, int> allDepths,
            IReadOnlyDictionary<string, int> allCooldowns,
            List<string> sortedModIds)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Queue.PerMod", "Per-Mod Queues"));

            if (sortedModIds.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label(UiText.Get("RimMind.Settings.Queue.NoMods", "No registered mod queues"));
                GUI.color = Color.white;
            }
            else
            {
                string cdLabel = UiText.Get("RimMind.Settings.Queue.Cooldown", "Cooldown");
                string readyLabel = UiText.Get("RimMind.Settings.Queue.Ready", "Ready");
                string qCountLabel = UiText.Get("RimMind.Settings.Queue.QueueCount", "Queued");

                for (int i = 0; i < sortedModIds.Count; i++)
                {
                    string modId = sortedModIds[i];
                    int depth = allDepths.TryGetValue(modId, out var d) ? d : 0;
                    int cooldownLeft = allCooldowns.TryGetValue(modId, out var cd) ? cd : queue.GetCooldownTicksLeft(modId);
                    float cooldownSec = cooldownLeft / 60f;

                    string cooldownStr = cooldownLeft > 0
                        ? $"{cdLabel}: {cooldownSec:F1}s"
                        : readyLabel;
                    string depthStr = depth > 0
                        ? $"  [{qCountLabel}: {depth}]"
                        : "";

                    GUI.color = cooldownLeft > 0 ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
                    listing.Label($"{modId}  {cooldownStr}{depthStr}");
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawActiveRequests(Listing_Standard listing, IRequestQueue queue)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Queue.ActiveRequests", "Active Requests"));

            var activeRequests = queue.GetActiveRequests();
            if (activeRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label(UiText.Get("RimMind.Settings.Queue.NoActive", "No active requests"));
                GUI.color = Color.white;
            }
            else
            {
                string priorityLabel = UiText.Get("RimMind.Settings.Queue.Priority", "Priority");
                string attemptLabel = UiText.Get("RimMind.Settings.Queue.Attempt", "Attempt");
                string elapsedLabel = UiText.Get("RimMind.Settings.Queue.Elapsed", "Elapsed");
                int limit = Mathf.Min(activeRequests.Count, MaxDisplayedRequests);
                for (int i = 0; i < limit; i++)
                {
                    var req = activeRequests[i];
                    int elapsedTicks = Find.TickManager.TicksGame - req.StartedProcessingAtTick;
                    float elapsedSec = elapsedTicks / 60f;
                    string priority = req.Envelope.Priority.ToString();
                    string info = $"[{req.Envelope.ModId}] {req.Envelope.RequestId}  " +
                                  $"{priorityLabel}: {priority}  " +
                                  $"{attemptLabel}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{elapsedLabel}: {elapsedSec:F1}s";
                    GUI.color = new Color(0.7f, 0.85f, 1f);
                    listing.Label(info);
                }
                if (activeRequests.Count > MaxDisplayedRequests)
                {
                    int remaining = activeRequests.Count - MaxDisplayedRequests;
                    GUI.color = Color.gray;
                    listing.Label(FormatMoreRequests(remaining));
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawQueuedRequests(Listing_Standard listing, IRequestQueue queue)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Queue.QueuedRequests", "Queued Requests"));

            var queuedRequests = queue.GetAllQueuedRequests();
            if (queuedRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label(UiText.Get("RimMind.Settings.Queue.NoQueued", "No queued requests"));
                GUI.color = Color.white;
            }
            else
            {
                string priorityLabel = UiText.Get("RimMind.Settings.Queue.Priority", "Priority");
                string attemptLabel = UiText.Get("RimMind.Settings.Queue.Attempt", "Attempt");
                string waitingLabel = UiText.Get("RimMind.Settings.Queue.Waiting", "Waiting");
                int limit = Mathf.Min(queuedRequests.Count, MaxDisplayedRequests);
                for (int i = 0; i < limit; i++)
                {
                    var req = queuedRequests[i];
                    int waitTicks = Find.TickManager.TicksGame - req.EnqueuedAtTick;
                    float waitSec = waitTicks / 60f;
                    string priority = req.Envelope.Priority.ToString();
                    string info = $"[{req.Envelope.ModId}] {req.Envelope.RequestId}  " +
                                  $"{priorityLabel}: {priority}  " +
                                  $"{attemptLabel}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{waitingLabel}: {waitSec:F1}s";
                    GUI.color = new Color(0.85f, 0.85f, 0.7f);
                    listing.Label(info);
                }
                if (queuedRequests.Count > MaxDisplayedRequests)
                {
                    int remaining = queuedRequests.Count - MaxDisplayedRequests;
                    GUI.color = Color.gray;
                    listing.Label(FormatMoreRequests(remaining));
                }
            }
            GUI.color = Color.white;
        }

        private static string FormatMoreRequests(int remaining)
        {
            const string key = "RimMind.Settings.Queue.MoreRequests";
            string formatted = UiText.Format(key, remaining);
            return formatted == key ? $"... +{remaining} more requests" : formatted;
        }
    }
}
