using System;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class SettingsEntryDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private ISettingsProvider? _settings;
        private IRequestQueue? _requestQueue;
        private IAgentLoopScheduler? _agentLoopScheduler;

        public IDisposable? Bind(RuntimeServiceScope scope)
        {
            _settings = scope.GetOptional<ISettingsProvider>();
            _requestQueue = scope.GetOptional<IRequestQueue>();
            _agentLoopScheduler = scope.GetOptional<IAgentLoopScheduler>();
            return null;
        }

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Hub:SettingsEntry");

            float y = rect.y;
            y = RimMindUI.DrawSectionHeader(rect, y, "RimMind.UI.Hub.SettingsEntryTitle".Translate(), scope.Recorder);
            y = RimMindUI.DrawWrappedLabel(
                rect,
                y,
                "RimMind.UI.Hub.SettingsEntryDescription".Translate(),
                RimMindUI.ColorValue,
                scope.Recorder);

            y += RimMindUI.SectionGap * 0.5f;

            ISettingsProvider? settings = _settings ?? RuntimeServiceHub.Shared.Capture().GetOptional<ISettingsProvider>();
            IRequestQueue? queue = _requestQueue ?? RuntimeServiceHub.Shared.Capture().GetOptional<IRequestQueue>();
            IAgentLoopScheduler? scheduler = _agentLoopScheduler ?? RuntimeServiceHub.Shared.Capture().GetOptional<IAgentLoopScheduler>();

            string providerName = FormatProvider(settings?.Provider);
            string modelName = settings?.ModelName ?? "-";

            AgentLoopSnapshot loop = scheduler?.GetSnapshot() ?? AgentLoopSnapshot.Empty;
            string healthText;
            Color healthColor;
            if (loop.FaultedAgents > 0)
            {
                healthText = $"{loop.FaultedAgents} {"RimMind.UI.AgentsPage.Trace.Error".Translate()}";
                healthColor = RimMindUI.ColorError;
            }
            else if (loop.PausedAgents > 0)
            {
                healthText = $"{loop.PausedAgents} {"RimMind.Agent.State.Paused".Translate()}";
                healthColor = RimMindUI.ColorPaused;
            }
            else if (loop.ActiveAgents > 0)
            {
                healthText = "RimMind.Agent.State.Active".Translate();
                healthColor = RimMindUI.ColorActive;
            }
            else
            {
                healthText = "RimMind.Prompt.Health.Healthy".Translate();
                healthColor = RimMindUI.ColorActive;
            }

            int queued = queue?.TotalQueuedCount ?? 0;
            int active = queue?.ActiveRequestCount ?? 0;
            string queueText = $"{queued} ({"RimMind.UI.Hub.ActiveRequests".Translate()}: {active})";

            // Status Card with blue accent bar
            Rect cardRect = new Rect(rect.x + RimMindUI.Padding, y, rect.width - RimMindUI.Padding * 2f, 76f);
            Widgets.DrawBoxSolid(cardRect, RimMindUI.ColorCardBg);
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, 4f, cardRect.height), new Color(0.4f, 0.7f, 1.0f, 0.85f));
            scope.Record(cardRect, "Hub:SettingsEntry:StatusCard");

            float cardInnerY = cardRect.y + 10f;
            float labelX = cardRect.x + 14f;
            float cardContentW = cardRect.width - 28f;
            float colW = (cardContentW - 16f) / 3f;

            // Col 1: Provider & Model (with Tooltip)
            Rect col1 = new Rect(labelX, cardInnerY, colW, 52f);
            Text.Font = GameFont.Tiny;
            GUI.color = RimMindUI.ColorSectionTitle;
            Widgets.Label(new Rect(col1.x, col1.y, col1.width, 18f), "RimMind.Settings.Provider".Translate());
            Text.Font = GameFont.Small;
            GUI.color = RimMindUI.ColorValue;
            Widgets.Label(new Rect(col1.x, col1.y + 18f, col1.width, 22f), $"{providerName} ({modelName})");
            TooltipHandler.TipRegion(col1, $"Endpoint: {settings?.ApiEndpoint ?? "-"} | Model: {modelName}");

            // Col 2: Health (with Tooltip)
            Rect col2 = new Rect(col1.xMax + 8f, cardInnerY, colW, 52f);
            Text.Font = GameFont.Tiny;
            GUI.color = RimMindUI.ColorSectionTitle;
            Widgets.Label(new Rect(col2.x, col2.y, col2.width, 18f), "RimMind.Context.IncludeHealth".Translate());
            Text.Font = GameFont.Small;
            GUI.color = healthColor;
            Widgets.Label(new Rect(col2.x, col2.y + 18f, col2.width, 22f), healthText);
            TooltipHandler.TipRegion(col2, $"Active: {loop.ActiveAgents}, Paused: {loop.PausedAgents}, Faults: {loop.FaultedAgents}");

            // Col 3: Queue Count (with Tooltip)
            Rect col3 = new Rect(col2.xMax + 8f, cardInnerY, colW, 52f);
            Text.Font = GameFont.Tiny;
            GUI.color = RimMindUI.ColorSectionTitle;
            Widgets.Label(new Rect(col3.x, col3.y, col3.width, 18f), "RimMind.UI.Hub.QueueCountLabel".Translate());
            Text.Font = GameFont.Small;
            GUI.color = queued > 0 ? RimMindUI.ColorActive : RimMindUI.ColorMuted;
            Widgets.Label(new Rect(col3.x, col3.y + 18f, col3.width, 22f), queueText);
            TooltipHandler.TipRegion(col3, $"Queued: {queued}, Active: {active}, Paused: {queue?.IsPaused == true}");

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            y = cardRect.yMax + RimMindUI.SectionGap;
            y = RimMindUI.DrawSectionHeader(rect, y, "RimMind.UI.Hub.ShortcutsTitle".Translate(), scope.Recorder);

            float btnH = RimMindUI.BtnHeight;
            float btnGap = 12f;
            float btn1W = 180f;
            float btn2W = 260f;

            Rect btnOpenSettings = new Rect(rect.x + RimMindUI.Padding, y, btn1W, btnH);
            scope.Record(btnOpenSettings, "Hub:SettingsEntry:OpenSettings");
            if (Widgets.ButtonText(btnOpenSettings, "RimMind.UI.Hub.OpenSettings".Translate()))
            {
                OpenSettings();
            }
            TooltipHandler.TipRegion(btnOpenSettings, "RimMind.UI.Hub.OpenSettings".Translate());

            Rect btnOpenInspector = new Rect(btnOpenSettings.xMax + btnGap, y, btn2W, btnH);
            scope.Record(btnOpenInspector, "Hub:SettingsEntry:OpenContextInspector");
            if (Widgets.ButtonText(btnOpenInspector, "RimMind.Settings.OpenContextPayloadInspector".Translate()))
            {
                Pawn? pawn = context.SelectedPawn ?? Find.Selector.SingleSelectedThing as Pawn;
                Find.WindowStack.Add(new Window_ContextPayloadInspector(pawn));
            }
            TooltipHandler.TipRegion(btnOpenInspector, "RimMind.UI.Hub.InspectPayloadTip".Translate());
        }

        private void OpenSettings()
        {
            Find.WindowStack.Add(new Window_RimMindSettings());
        }

        private static string FormatProvider(string? providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return "Unknown";

            string normalized = providerId.ToLowerInvariant() switch
            {
                "openai" => "OpenAI",
                "player2" => "Player2",
                _ => providerId
            };
            string key = $"RimMind.Settings.Provider.{normalized}";
            var translation = key.Translate();
            return translation == key ? providerId : translation;
        }
    }
}
