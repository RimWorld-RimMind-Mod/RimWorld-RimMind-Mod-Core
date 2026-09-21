using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Api;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static partial class ApiTabDrawer
    {
        private static bool _showApiKey;
        private static string _testStatus = "";
        private static Color _testStatusColor = Color.white;
        private static bool _testPending;
        private static Vector2 _apiScroll;
        private static string _presetAppliedMessage = "";
        private static int _presetAppliedUntilTick;
        private static readonly GenerationUiState GenerationState = new GenerationUiState();

        private static readonly RuntimeServiceRef<IExtensionRegistry<IAIClientFactory>> ProviderRegistry =
            RuntimeServiceRef<IExtensionRegistry<IAIClientFactory>>.Required();
        private static readonly RuntimeServiceRef<ISettingsProvider> SettingsProvider =
            RuntimeServiceRef<ISettingsProvider>.Required();
        private static readonly RuntimeServiceRef<IPlayer2Lifecycle> Player2Lifecycle =
            RuntimeServiceRef<IPlayer2Lifecycle>.Optional();
        private static readonly RuntimeServiceRef<IClientManager> ClientManager =
            RuntimeServiceRef<IClientManager>.Optional();
        private static readonly RuntimeServiceRef<IRequestQueue> RequestQueue =
            RuntimeServiceRef<IRequestQueue>.Optional();

        public static void Draw(
            Rect inRect,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            var providerRegistry = ProviderRegistry.Resolve(runtimeScope);
            var player2Lifecycle = Player2Lifecycle.ResolveOptional(runtimeScope);
            if (GenerationState.Refresh(runtimeScope.Generation))
            {
                _activeConnectionTest = null;
                _testPending = false;
                _testStatus = string.Empty;
                _testStatusColor = Color.white;
                _presetAppliedMessage = string.Empty;
                GenerationState.MarkDerivedState();
            }

            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 5, rowsPerSection: 4);
            float contentH = Mathf.Max(EstimateApiHeight(), formLayout.ContentHeight);
            Rect viewRect = new Rect(0f, 0f, formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth, contentH);
            Widgets.BeginScrollView(inRect, ref _apiScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Api:Viewport");
            scope?.Record(viewRect, "Settings:Api:Content");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Tab.Api".Translate());

            DrawPresetsBar(listing, s);

            DrawConnectionSection(listing, s, runtimeScope, providerRegistry, player2Lifecycle, scope);
            DrawGenerationSection(listing, s, scope);
            DrawPerformanceSection(listing, s, runtimeScope, scope);
            DrawInterfaceSection(listing, s, scope);

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPresetsBar(Listing_Standard listing, ISettingsProvider s)
        {
            listing.Label("RimMind.Settings.PresetsBarTitle".Translate());
            Rect barRect = listing.GetRect(28f);
            float gap = 6f;
            float btnW = (barRect.width - gap * 2f) / 3f;

            // Preset 1: High Responsive
            Rect btn1 = new Rect(barRect.x, barRect.y, btnW, barRect.height);
            string label1 = "RimMind.Settings.Preset.Responsive".Translate();
            if (Widgets.ButtonText(btn1, label1))
            {
                ApplyPresetResponsive(s);
            }
            TooltipHandler.TipRegion(btn1, "RimMind.Settings.Preset.Responsive.Desc".Translate());

            // Preset 2: Balanced
            Rect btn2 = new Rect(btn1.xMax + gap, barRect.y, btnW, barRect.height);
            string label2 = "RimMind.Settings.Preset.Balanced".Translate();
            if (Widgets.ButtonText(btn2, label2))
            {
                ApplyPresetBalanced(s);
            }
            TooltipHandler.TipRegion(btn2, "RimMind.Settings.Preset.Balanced.Desc".Translate());

            // Preset 3: Eco
            Rect btn3 = new Rect(btn2.xMax + gap, barRect.y, btnW, barRect.height);
            string label3 = "RimMind.Settings.Preset.Eco".Translate();
            if (Widgets.ButtonText(btn3, label3))
            {
                ApplyPresetEco(s);
            }
            TooltipHandler.TipRegion(btn3, "RimMind.Settings.Preset.Eco.Desc".Translate());

            if (!string.IsNullOrEmpty(_presetAppliedMessage) && Environment.TickCount < _presetAppliedUntilTick)
            {
                Color prevColor = GUI.color;
                try
                {
                    listing.Gap(2f);
                    GUI.color = new Color(0.4f, 0.9f, 0.4f);
                    listing.Label(_presetAppliedMessage);
                }
                finally
                {
                    GUI.color = prevColor;
                }
            }
            listing.Gap(6f);
        }

        internal static void ApplyPresetResponsive(ISettingsProvider s)
        {
            s.MaxTokens = 600;
            s.MaxConcurrentRequests = 3;
            s.RequestTimeoutMs = 25000;
            s.DefaultModCooldownTicks = 15 * 60;
            s.Persist();
            string label = "RimMind.Settings.Preset.Responsive".Translate();
            _presetAppliedMessage = "RimMind.Settings.PresetApplied".Translate(label);
            _presetAppliedUntilTick = Environment.TickCount + 3500;
        }

        internal static void ApplyPresetBalanced(ISettingsProvider s)
        {
            s.MaxTokens = 800;
            s.MaxConcurrentRequests = 2;
            s.RequestTimeoutMs = 45000;
            s.DefaultModCooldownTicks = 30 * 60;
            s.Persist();
            string label = "RimMind.Settings.Preset.Balanced".Translate();
            _presetAppliedMessage = "RimMind.Settings.PresetApplied".Translate(label);
            _presetAppliedUntilTick = Environment.TickCount + 3500;
        }

        internal static void ApplyPresetEco(ISettingsProvider s)
        {
            s.MaxTokens = 400;
            s.MaxConcurrentRequests = 1;
            s.RequestTimeoutMs = 60000;
            s.DefaultModCooldownTicks = 60 * 60;
            s.Persist();
            string label = "RimMind.Settings.Preset.Eco".Translate();
            _presetAppliedMessage = "RimMind.Settings.PresetApplied".Translate(label);
            _presetAppliedUntilTick = Environment.TickCount + 3500;
        }

        internal static string? CurrentPresetFeedback => _presetAppliedMessage;


        private static void DrawCardHeader(Listing_Standard listing, string title)
        {
            listing.Gap(10f);
            Rect headerRect = listing.GetRect(28f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.14f, 0.17f, 0.24f, 0.75f));
            Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), new Color(0.4f, 0.7f, 1.0f, 0.9f));

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.88f, 0.94f, 1.0f);
            Rect textRect = new Rect(headerRect.x + 12f, headerRect.y + 4f, headerRect.width - 24f, headerRect.height - 4f);
            Widgets.Label(textRect, title);
            GUI.color = Color.white;
            listing.Gap(6f);
        }

        private static void DrawConnectionSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            IExtensionRegistry<IAIClientFactory> providerRegistry,
            IPlayer2Lifecycle? player2Lifecycle,
            RimMindLayoutScope? scope = null)
        {
            DrawCardHeader(listing, "RimMind.Settings.Section.Connection".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Connection");

            listing.Label("RimMind.Settings.Provider".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Provider.Desc".Translate());
            GUI.color = Color.white;
            {
                Rect row = listing.GetRect(28f);
                if (Widgets.ButtonText(row, GetProviderLabel(s.Provider)))
                {
                    var options = new List<FloatMenuOption>();
                    var allProviders = AIProviderRegistry.GetAllProviderIds(providerRegistry);
                    foreach (var p in allProviders)
                    {
                        var label = GetProviderLabel(p);
                        options.Add(new FloatMenuOption(label, () =>
                        {
                            RuntimeServiceScope operationScope = RuntimeServiceHub.Shared.Capture();
                            var currentSettings = SettingsProvider.Resolve(operationScope);
                            var currentRegistry = ProviderRegistry.Resolve(operationScope);
                            var currentPlayer2Lifecycle = Player2Lifecycle.ResolveOptional(operationScope);
                            var currentClientManager = ClientManager.ResolveOptional(operationScope);
                            var prev = currentSettings.Provider;
                            currentSettings.Provider = p;
                            currentSettings.Persist();
                            if (!AIProviderRegistry.RequiresApiKey(p, currentRegistry))
                                currentPlayer2Lifecycle?.CheckStatusAndNotify();
                            if (prev != p)
                                currentClientManager?.InvalidateCache();
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            listing.Gap(6f);

            if (AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry))
            {
                DrawApiKeySection(listing, s, scope);
            }
            else
            {
                DrawPlayer2Section(listing, s, player2Lifecycle, scope);
            }

            listing.Gap(10f);

            DrawConnectionTestButton(listing, s, runtimeScope, providerRegistry);

            if (IsModelServiceInstalled())
            {
                listing.Gap(4f);
                GUI.color = new Color(0.5f, 0.9f, 0.6f);
                listing.Label("RimMind.Settings.ModelServiceActiveNote".Translate());
                GUI.color = Color.white;
            }
            else
            {
                listing.Gap(4f);
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.ModelServiceNotInstalledNote".Translate());
                GUI.color = Color.white;
            }
        }

        private static bool IsModelServiceInstalled()
        {
            try
            {
                var tabs = RimMindAPI.Ext.Get<ISettingsTab>();
                return tabs?.FindById("model_service") != null || ModsConfig.IsActive("mcocdaa.RimMindModelService");
            }
            catch
            {
                return false;
            }
        }

        private static void DrawGenerationSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            DrawCardHeader(listing, "RimMind.Settings.Section.Generation".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Generation");

            listing.Label($"{"RimMind.Settings.MaxTokens".Translate()}: {s.MaxTokens}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.MaxTokens.Desc".Translate());
            GUI.color = Color.white;
            s.MaxTokens = (int)listing.Slider(s.MaxTokens, 200f, 2000f);

            listing.Label($"{"RimMind.Settings.Temperature".Translate()}: {s.DefaultTemperature:F2}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Temperature.Desc".Translate());
            GUI.color = Color.white;
            s.DefaultTemperature = listing.Slider(s.DefaultTemperature, 0f, 2f);

            listing.Gap(4f);
            var forceJsonMode = s.ForceJsonMode;
            listing.CheckboxLabeled(
                "RimMind.Settings.ForceJsonMode".Translate(),
                ref forceJsonMode,
                "RimMind.Settings.ForceJsonModeDesc".Translate());
            s.ForceJsonMode = forceJsonMode;

            listing.Gap(6f);
            listing.Label("RimMind.UI.FlywheelAutoApply".Translate());
            {
                Rect row = listing.GetRect(28f);
                if (Widgets.ButtonText(row, GetAutoApplyModeLabel(s.AutoApplyMode)))
                {
                    var modes = new List<FloatMenuOption>();
                    foreach (FlywheelAutoApplyMode mode in Enum.GetValues(typeof(FlywheelAutoApplyMode)))
                    {
                        var label = GetAutoApplyModeLabel(mode);
                        modes.Add(new FloatMenuOption(label, () => s.AutoApplyMode = mode));
                    }
                    Find.WindowStack.Add(new FloatMenu(modes));
                }
            }

            listing.Label("RimMind.UI.FlywheelConfidence".Translate(s.AutoApplyConfidenceThreshold));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.UI.FlywheelConfidence.Desc".Translate());
            GUI.color = Color.white;
            s.AutoApplyConfidenceThreshold = listing.Slider(s.AutoApplyConfidenceThreshold, 0.5f, 1.0f);
        }

        private static void DrawPerformanceSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            DrawCardHeader(listing, "RimMind.Settings.Section.Performance".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Performance");

            listing.Label($"{"RimMind.Settings.MaxConcurrent".Translate()}: {s.MaxConcurrentRequests}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.MaxConcurrent.Desc".Translate());
            GUI.color = Color.white;
            s.MaxConcurrentRequests = (int)listing.Slider(s.MaxConcurrentRequests, 1f, 10f);

            listing.Label($"{"RimMind.Settings.MaxRetry".Translate()}: {s.MaxRetryCount}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.MaxRetry.Desc".Translate());
            GUI.color = Color.white;
            s.MaxRetryCount = (int)listing.Slider(s.MaxRetryCount, 0f, 5f);

            listing.Label($"{"RimMind.Settings.RequestTimeout".Translate()}: {s.RequestTimeoutMs / 1000}s");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.RequestTimeout.Desc".Translate());
            GUI.color = Color.white;
            s.RequestTimeoutMs = (int)listing.Slider(s.RequestTimeoutMs / 1000f, 10f, 300f) * 1000;

            listing.Label($"{"RimMind.Settings.RequestExpireTicks".Translate()}: {s.RequestExpireTicks / 60f:F0}s ({s.RequestExpireTicks} ticks)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.RequestExpireTicks.Desc".Translate());
            GUI.color = Color.white;
            s.RequestExpireTicks = (int)listing.Slider(s.RequestExpireTicks, 6000f, 120000f);

            listing.Label($"{"RimMind.Settings.BehaviorHistoryMax".Translate()}: {s.BehaviorHistoryMax}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());
            GUI.color = Color.white;
            s.BehaviorHistoryMax = (int)listing.Slider(s.BehaviorHistoryMax, 10f, 500f);

            listing.Label($"{"RimMind.Settings.QueueProcessInterval".Translate()}: {s.QueueProcessInterval} ticks ({s.QueueProcessInterval / 60f:F1}s)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.QueueProcessInterval.Desc".Translate());
            GUI.color = Color.white;
            s.QueueProcessInterval = (int)listing.Slider(s.QueueProcessInterval, 10f, 300f);

            listing.Label($"{"RimMind.Settings.DefaultModCooldown".Translate()}: {s.DefaultModCooldownTicks / 60f:F0}s ({s.DefaultModCooldownTicks} ticks)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.DefaultModCooldown.Desc".Translate());
            GUI.color = Color.white;
            s.DefaultModCooldownTicks = (int)listing.Slider(s.DefaultModCooldownTicks, 600f, 36000f);

            var queue = RequestQueue.ResolveOptional(runtimeScope);
            if (queue != null)
            {
                listing.Gap(4f);
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.QueueSeeTab".Translate());
                GUI.color = Color.white;
            }
        }

        private static void DrawInterfaceSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            DrawCardHeader(listing, "RimMind.Settings.Section.Interface".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Interface");

            var autoHide = s.RequestOverlayAutoHideWhenEmpty;
            listing.CheckboxLabeled(
                "RimMind.Settings.RequestOverlayAutoHideWhenEmpty".Translate(),
                ref autoHide,
                "RimMind.Settings.RequestOverlayAutoHideWhenEmpty.Desc".Translate());
            s.RequestOverlayAutoHideWhenEmpty = autoHide;

            var showProgress = s.ShowAgentProgressFloat;
            listing.CheckboxLabeled(
                "RimMind.Settings.ShowAgentProgressFloat".Translate(),
                ref showProgress,
                "RimMind.Settings.ShowAgentProgressFloat.Desc".Translate());
            s.ShowAgentProgressFloat = showProgress;

            var mentalMonitor = s.EnableFloatingMentalMonitor;
            listing.CheckboxLabeled(
                "RimMind.Settings.EnableFloatingMentalMonitor".Translate(),
                ref mentalMonitor,
                "RimMind.Settings.EnableFloatingMentalMonitor.Desc".Translate());
            s.EnableFloatingMentalMonitor = mentalMonitor;

            listing.Gap(6f);
            Rect inspectorBtnRect = listing.GetRect(30f);
            if (Widgets.ButtonText(inspectorBtnRect, "RimMind.Settings.OpenContextPayloadInspector".Translate()))
            {
                Find.WindowStack.Add(new Window_ContextPayloadInspector());
            }
            TooltipHandler.TipRegion(inspectorBtnRect, "RimMind.Settings.OpenContextPayloadInspector.Desc".Translate());

            listing.Gap(6f);
            var debugLogging = s.DebugLogging;
            listing.CheckboxLabeled(
                "RimMind.Settings.DebugLogging".Translate(),
                ref debugLogging,
                "RimMind.Settings.DebugLogging.Desc".Translate());
            s.DebugLogging = debugLogging;
        }

        private static void DrawApiKeySection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            listing.Label("RimMind.Settings.ApiKey".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ApiKey.Desc".Translate());
            GUI.color = Color.white;
            {
                Rect row = listing.GetRect(26f);
                float btnW = 52f;
                Rect field = new Rect(row.x, row.y, row.width - btnW - 4f, row.height);
                Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

                if (_showApiKey)
                {
                    s.ApiKey = Widgets.TextField(field, s.ApiKey ?? string.Empty);
                }
                else
                {
                    string hiddenLabel = string.IsNullOrEmpty(s.ApiKey)
                        ? string.Empty
                        : "RimMind.Settings.ApiKey.Saved".Translate(s.ApiKey.Length).ToString();
                    Widgets.DrawBoxSolid(field, new Color(0.08f, 0.08f, 0.08f, 0.35f));
                    Widgets.Label(new Rect(field.x + 6f, field.y + 4f, field.width - 12f, field.height), hiddenLabel);
                }
                if (Widgets.ButtonText(toggle, _showApiKey ? "RimMind.Settings.Hide".Translate() : "RimMind.Settings.Show".Translate()))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            listing.Label("RimMind.Settings.ApiEndpoint".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ApiEndpoint.Desc".Translate());
            GUI.color = Color.white;
            s.ApiEndpoint = listing.TextEntry(s.ApiEndpoint);

            listing.Gap(4f);
            listing.Label("RimMind.Settings.ModelName".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ModelName.Desc".Translate());
            GUI.color = Color.white;
            s.ModelName = listing.TextEntry(s.ModelName);
        }

        private static void DrawPlayer2Section(
            Listing_Standard listing,
            ISettingsProvider s,
            IPlayer2Lifecycle? player2Lifecycle,
            RimMindLayoutScope? scope = null)
        {
            GUI.color = Color.gray;
            listing.Label("RimMind.Settings.Player2.Desc".Translate());
            GUI.color = Color.white;
            listing.Gap(4f);

            listing.Label("RimMind.Settings.ApiKey".Translate() + " (" + "RimMind.Settings.Player2.ApiKeyOptional".Translate() + ")");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Player2.ApiKeyDesc".Translate());
            GUI.color = Color.white;
            {
                Rect row = listing.GetRect(26f);
                float btnW = 52f;
                Rect field = new Rect(row.x, row.y, row.width - btnW - 4f, row.height);
                Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

                if (_showApiKey)
                {
                    s.ApiKey = Widgets.TextField(field, s.ApiKey ?? string.Empty);
                }
                else
                {
                    string hiddenLabel = string.IsNullOrEmpty(s.ApiKey)
                        ? string.Empty
                        : "RimMind.Settings.ApiKey.Saved".Translate(s.ApiKey.Length).ToString();
                    Widgets.DrawBoxSolid(field, new Color(0.08f, 0.08f, 0.08f, 0.35f));
                    Widgets.Label(new Rect(field.x + 6f, field.y + 4f, field.width - 12f, field.height), hiddenLabel);
                }
                if (Widgets.ButtonText(toggle, _showApiKey ? "RimMind.Settings.Hide".Translate() : "RimMind.Settings.Show".Translate()))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            {
                Rect checkBtnRow = listing.GetRect(28f);
                if (Widgets.ButtonText(checkBtnRow, "RimMind.Settings.Player2.CheckLocal".Translate()))
                    player2Lifecycle?.CheckStatusAndNotify();
            }

            listing.Gap(4f);
            listing.Label("RimMind.Settings.Player2.RemoteUrl".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Player2.RemoteUrl.Desc".Translate());
            GUI.color = Color.white;
            s.Player2RemoteUrl = listing.TextEntry(s.Player2RemoteUrl);

            listing.Gap(4f);
            {
                float balance = player2Lifecycle?.CachedBalance ?? -1;
                string balanceText = balance >= 0
                    ? $"Joules: {balance:F2}"
                    : "RimMind.Settings.Player2.BalanceUnknown".Translate();
                listing.Label(balanceText);

                Rect refreshRow = listing.GetRect(28f);
                if (Widgets.ButtonText(refreshRow, "RimMind.Settings.Player2.RefreshBalance".Translate()))
                    player2Lifecycle?.RefreshBalance();
            }
        }

        private static void DrawConnectionTestButton(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            IExtensionRegistry<IAIClientFactory> providerRegistry)
        {
            Rect row = listing.GetRect(28f);
            Rect btn = new Rect(row.x, row.y, 110f, row.height);
            Rect status = new Rect(btn.xMax + 8f, row.y + 4f, row.width - 120f, row.height);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !_testPending;
            bool testClicked = Widgets.ButtonText(btn, "RimMind.Settings.TestConnection".Translate());
            GUI.enabled = wasEnabled;
            if (testClicked)
                RunConnectionTest(s, runtimeScope, providerRegistry);
            GUI.color = _testStatusColor;
            Widgets.Label(status, _testStatus);
            GUI.color = Color.white;
        }
    }
}
