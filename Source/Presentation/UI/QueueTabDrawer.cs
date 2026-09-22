using System.Collections.Generic;
using System.Linq;
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
        private static Vector2 _queueScroll;
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
                listing0.Label("RimMind.Settings.QueueNotAvailable".Translate());
                GUI.color = Color.white;
                listing0.End();
                return;
            }

            var allDepths = queue.GetAllQueueDepths();
            var allCooldowns = queue.GetAllCooldowns();
            var allModIds = new HashSet<string>(allDepths.Keys);
            allModIds.UnionWith(allCooldowns.Keys);

            int modCount = allModIds.Count;
            int activeCount = queue.ActiveRequestCount;
            int queuedCount = queue.TotalQueuedCount;
            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 5, rowsPerSection: 4);
            float contentH = 60f + 28f + modCount * 26f + 28f + activeCount * 24f + 28f + queuedCount * 24f + 80f;
            contentH = Mathf.Max(contentH, formLayout.ContentHeight);
            contentH = Mathf.Max(contentH, inRect.height + 10f);

            Rect viewRect = new Rect(0f, 0f, formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth, contentH);
            Widgets.BeginScrollView(inRect, ref _queueScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Queue:Viewport");
            scope?.Record(viewRect, "Settings:Queue:Content");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawQueueStatus(listing, queue, settings);
            DrawQueueControls(listing, queue);
            DrawPerModCooldowns(listing, queue, allModIds);
            DrawActiveRequests(listing, queue);
            DrawQueuedRequests(listing, queue);

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawQueueStatus(Listing_Standard listing, IRequestQueue queue, ISettingsProvider settings)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Queue.Status".Translate());

            string pauseLabel = queue.IsPaused
                ? "RimMind.Settings.QueuePaused".Translate()
                : "RimMind.Settings.QueueRunning".Translate();
            GUI.color = queue.IsPaused ? Color.yellow : new Color(0.4f, 0.9f, 0.4f);
            listing.Label(pauseLabel);
            GUI.color = Color.white;

            listing.Label($"{"RimMind.Settings.Queue.Active".Translate()}: {queue.ActiveRequestCount} / {settings.MaxConcurrentRequests}");
            listing.Label($"{"RimMind.Settings.Queue.Queued".Translate()}: {queue.TotalQueuedCount}");
            GUI.color = queue.IsLocalModelBusy ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
            listing.Label($"{"RimMind.Settings.Queue.LocalModel".Translate()}: {(queue.IsLocalModelBusy ? "RimMind.Settings.Queue.Busy".Translate() : "RimMind.Settings.Queue.Idle".Translate())}");
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
                ? "RimMind.Settings.Queue.Resume".Translate()
                : "RimMind.Settings.Queue.Pause".Translate();
            if (Widgets.ButtonText(pauseBtn, pauseText))
            {
                if (queue.IsPaused) queue.ResumeQueue();
                else queue.PauseQueue();
            }
            TooltipHandler.TipRegion(pauseBtn, pauseText);
            if (Widgets.ButtonText(clearBtn, "RimMind.Settings.Queue.ClearQueues".Translate()))
                queue.ClearAllQueues();
            TooltipHandler.TipRegion(clearBtn, "RimMind.Settings.Queue.ClearQueues".Translate());
            if (Widgets.ButtonText(clearCdBtn, "RimMind.Settings.Queue.ClearCooldowns".Translate()))
                queue.ClearAllCooldowns();
            TooltipHandler.TipRegion(clearCdBtn, "RimMind.Settings.Queue.ClearCooldowns".Translate());
        }

        private static void DrawPerModCooldowns(Listing_Standard listing, IRequestQueue queue, HashSet<string> allModIds)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Queue.PerMod".Translate());

            if (allModIds.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.Queue.NoMods".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (string modId in allModIds.OrderBy(id => id))
                {
                    int depth = queue.GetAllQueueDepths().TryGetValue(modId, out var d) ? d : 0;
                    int cooldownLeft = queue.GetCooldownTicksLeft(modId);
                    float cooldownSec = cooldownLeft / 60f;

                    string cooldownStr = cooldownLeft > 0
                        ? $"{"RimMind.Settings.Queue.Cooldown".Translate()}: {cooldownSec:F1}s"
                        : "RimMind.Settings.Queue.Ready".Translate();
                    string depthStr = depth > 0
                        ? $"  [{"RimMind.Settings.Queue.QueueCount".Translate()}: {depth}]"
                        : "";

                    GUI.color = cooldownLeft > 0 ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
                    listing.Label($"{modId}  {cooldownStr}{depthStr}");
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawActiveRequests(Listing_Standard listing, IRequestQueue queue)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Queue.ActiveRequests".Translate());

            var activeRequests = queue.GetActiveRequests();
            if (activeRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.Queue.NoActive".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (var req in activeRequests)
                {
                    int elapsedTicks = Find.TickManager.TicksGame - req.StartedProcessingAtTick;
                    float elapsedSec = elapsedTicks / 60f;
                    string priority = req.Envelope.Priority.ToString();
                    string info = $"[{req.Envelope.ModId}] {req.Envelope.RequestId}  " +
                                  $"{"RimMind.Settings.Queue.Priority".Translate()}: {priority}  " +
                                  $"{"RimMind.Settings.Queue.Attempt".Translate()}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{"RimMind.Settings.Queue.Elapsed".Translate()}: {elapsedSec:F1}s";
                    GUI.color = new Color(0.7f, 0.85f, 1f);
                    listing.Label(info);
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawQueuedRequests(Listing_Standard listing, IRequestQueue queue)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Queue.QueuedRequests".Translate());

            var queuedRequests = queue.GetAllQueuedRequests();
            if (queuedRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.Queue.NoQueued".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (var req in queuedRequests)
                {
                    int waitTicks = Find.TickManager.TicksGame - req.EnqueuedAtTick;
                    float waitSec = waitTicks / 60f;
                    string priority = req.Envelope.Priority.ToString();
                    string info = $"[{req.Envelope.ModId}] {req.Envelope.RequestId}  " +
                                  $"{"RimMind.Settings.Queue.Priority".Translate()}: {priority}  " +
                                  $"{"RimMind.Settings.Queue.Attempt".Translate()}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{"RimMind.Settings.Queue.Waiting".Translate()}: {waitSec:F1}s";
                    GUI.color = new Color(0.85f, 0.85f, 0.7f);
                    listing.Label(info);
                }
            }
            GUI.color = Color.white;
        }
    }
}
