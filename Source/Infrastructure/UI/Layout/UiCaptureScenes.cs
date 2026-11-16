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
            scenes.Add(new UiCaptureScene("hub-capabilities-mechanisms-top", "capabilities", () => Hub("capabilities:mechanisms")));
            scenes.Add(new UiCaptureScene("hub-capabilities-mechanisms-bottom", "capabilities", () => Hub("capabilities:mechanisms", bottom: true)));
            scenes.Add(new UiCaptureScene("hub-capabilities-context_keys-top", "capabilities", () => Hub("capabilities:context_keys")));
            scenes.Add(new UiCaptureScene("hub-capabilities-context_keys-bottom", "capabilities", () => Hub("capabilities:context_keys", bottom: true)));
            scenes.Add(new UiCaptureScene("requests-rollout-top", "ai_requests", () => RolloutRequests(false)));
            scenes.Add(new UiCaptureScene("requests-rollout-bottom", "ai_requests", () => RolloutRequests(true)));
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
                    case CapabilitiesDebugCenterPageDrawer capabilities: capabilities.ScrollToBottom(); break;
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

        private static Window_RimMindHub RolloutRequests(bool bottom)
        {
            var window = Hub("ai_requests:rollout", bottom: bottom);
            Pawn pawn = SelectedPawn();
            string npcId = !string.IsNullOrWhiteSpace(pawn.ThingID) ? pawn.ThingID : $"NPC-{pawn.thingIDNumber}";
            DateTime baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var turn1 = new AIRequestTraceEntry
            {
                RequestId = "ui-capture-rollout-t1",
                TraceId = "ui-capture-rollout-ep1",
                RolloutId = "ui-capture-rollout-ep1",
                TurnIndex = 1,
                IsAgentRollout = true,
                ModId = "RimMind.Core",
                ScenarioId = "agent.decision",
                NpcId = npcId,
                Source = "PawnThinker",
                Model = "offline-fixture",
                State = AIRequestTraceState.Completed,
                PromptTokens = 845,
                CompletionTokens = 92,
                CachedTokens = 0,
                TokensUsed = 937,
                ElapsedMs = 640,
                StartedAtUtc = baseTime,
                FinishedAtUtc = baseTime.AddMilliseconds(640),
                SystemPrompt = "<layer_L0>\nCore rules\n</layer_L0>\n<layer_L1>\nIdentity\n</layer_L1>\n<layer_L4>\nPerception: Colonist needs indoor bed.\n</layer_L4>",
                UserPrompt = "Evaluate sleeping quarters and available structures.",
                ReasoningContent = "Querying colony spatial snapshot and buildable furniture first.",
                Response = "{\"thought\":\"Checking buildable beds and room topology.\"}",
                ToolCalls =
                {
                    new AIRequestToolCallTrace(
                        "call_1",
                        "query_buildable_structures",
                        true,
                        null,
                        "{\"category\":\"furniture\"}",
                        "{\"status\":\"ok\",\"colony_anchor\":{\"x\":118,\"z\":122,\"has_base\":true},\"rooms\":[{\"room_id\":12,\"role\":\"Bedroom\",\"beds\":1}]}")
                },
                Layers =
                {
                    new AIRequestLayerTrace("system", "L0", "Core safety and runtime rules.", 320, false),
                    new AIRequestLayerTrace("system", "L1", "Colonist identity and traits.", 180, false),
                    new AIRequestLayerTrace("user", "L4", "Current needs and bedroom shortage.", 110, false)
                }
            };

            var turn2 = new AIRequestTraceEntry
            {
                RequestId = "ui-capture-rollout-t2",
                TraceId = "ui-capture-rollout-ep1",
                RolloutId = "ui-capture-rollout-ep1",
                TurnIndex = 2,
                IsAgentRollout = true,
                ModId = "RimMind.Core",
                ScenarioId = "agent.decision",
                NpcId = npcId,
                Source = "PawnThinker",
                Model = "offline-fixture",
                State = AIRequestTraceState.Completed,
                PromptTokens = 960,
                CompletionTokens = 108,
                CachedTokens = 790,
                TokensUsed = 1068,
                ElapsedMs = 580,
                StartedAtUtc = baseTime.AddSeconds(1),
                FinishedAtUtc = baseTime.AddSeconds(1).AddMilliseconds(580),
                SystemPrompt = "<layer_L0>\nCore rules\n</layer_L0>\n<layer_L1>\nIdentity\n</layer_L1>\n<layer_L4>\nTool observation received.\n</layer_L4>",
                UserPrompt = "Place bed inside existing Bedroom cluster.",
                ReasoningContent = "Bedroom #12 has space next to existing bed; placing wooden Bed blueprint.",
                Response = "{\"thought\":\"Placing wooden bed in Bedroom #12.\"}",
                ToolCalls =
                {
                    new AIRequestToolCallTrace(
                        "call_2",
                        "place_blueprint",
                        true,
                        null,
                        "{\"building_def\":\"Bed\",\"location_hint\":\"indoor\"}",
                        "{\"status\":\"placed\",\"room_role\":\"Bedroom\",\"cluster_reason\":\"peer_bed_room_cluster\"}")
                },
                Layers =
                {
                    new AIRequestLayerTrace("system", "L0", "Core safety and runtime rules.", 320, true),
                    new AIRequestLayerTrace("system", "L1", "Colonist identity and traits.", 180, true),
                    new AIRequestLayerTrace("user", "L4", "Tool observation: Bedroom #12 open tiles.", 225, false)
                },
                ActionOutcome = new AIRequestActionOutcomeTrace(
                    "place_blueprint",
                    true,
                    "Placed wooden Bed blueprint in Bedroom #12.",
                    "Bed | indoor")
            };

            ((AIRequestsDebugCenterPageDrawer)window.CurrentDrawer).UseDisplaySnapshot(new[] { turn1, turn2 });
            return window;
        }
    }
}
