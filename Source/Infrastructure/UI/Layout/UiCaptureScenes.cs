using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Models.Debug;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.DebugCenter.Pages;
using Verse;

namespace RimMind.Infrastructure.UI.Layout
{
    // Only navigation and display fixtures live here. Every scene uses a production window/drawer.
    internal sealed class UiCaptureScene
    {
        public string Id { get; }
        public string PageId { get; }
        public int FrameCount { get; }
        public Func<RimMindWindowBase> Open { get; }

        public UiCaptureScene(string id, string pageId, Func<RimMindWindowBase> open, int frames = 1)
        {
            Id = id;
            PageId = pageId;
            Open = open;
            FrameCount = frames;
        }
    }

    internal static class UiCaptureScenes
    {
        public static IReadOnlyList<UiCaptureScene> Create()
        {
            var scenes = new List<UiCaptureScene>();
            foreach (var page in DebugCenterPageRegistry.GetAll())
            {
                string id = page.Id;
                scenes.Add(new UiCaptureScene("hub-" + id + "-top", id,
                    () => Hub(id), id == "settings" ? 3 : 1));
                if (id != "settings")
                    scenes.Add(new UiCaptureScene("hub-" + id + "-bottom", id, () => Hub(id, bottom: true)));
            }

            scenes.Add(new UiCaptureScene("agent-selected-top", "agents", () => Hub("agents", SelectedPawn()), 3));
            scenes.Add(new UiCaptureScene("agent-selected-bottom", "agents", () => Hub("agents", SelectedPawn(), true)));
            scenes.Add(new UiCaptureScene("requests-empty", "ai_requests", () => Requests(false, false)));
            scenes.Add(new UiCaptureScene("requests-long-top", "ai_requests", () => Requests(true, false)));
            scenes.Add(new UiCaptureScene("requests-long-bottom", "ai_requests", () => Requests(true, true)));
            scenes.Add(new UiCaptureScene("settings-queue-top", "queue", () => new Window_RimMindSettings(false)));
            scenes.Add(new UiCaptureScene("settings-queue-bottom", "queue", () => new Window_RimMindSettings(true)));
            return scenes;
        }

        private static Pawn SelectedPawn()
            => Find.CurrentMap?.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                ?? throw new InvalidOperationException("capture-no-colonist");

        private static Window_RimMindHub Hub(string page, Pawn? pawn = null, bool bottom = false)
        {
            var window = new Window_RimMindHub(page, pawn);
            if (bottom)
            {
                switch (window.CurrentDrawer)
                {
                    case OverviewDebugCenterPageDrawer overview: overview.ScrollToBottom(); break;
                    case DebugTablePageBase table: table.ScrollToBottom(); break;
                    case AIRequestsDebugCenterPageDrawer requests: requests.ScrollToBottom(); break;
                    case AgentsDebugCenterPageDrawer agents: agents.ScrollToBottom(); break;
                }
            }
            return window;
        }

        private static Window_RimMindHub Requests(bool longText, bool bottom)
        {
            var window = Hub("ai_requests", bottom: bottom);
            var entries = new List<AIRequestTraceEntry>();
            if (longText)
            {
                string body = string.Join("\n", Enumerable.Repeat(
                    "RimMind.UI.Hub.SettingsEntryDescription".Translate().ToString(), 24));
                entries.Add(new AIRequestTraceEntry
                {
                    RequestId = "ui-capture-display-only", Source = "ui-capture", Model = "offline-fixture",
                    State = AIRequestTraceState.Failed, SystemPrompt = body, UserPrompt = body,
                    Response = body, Error = body
                });
            }
            ((AIRequestsDebugCenterPageDrawer)window.CurrentDrawer).UseDisplaySnapshot(entries);
            return window;
        }
    }
}
