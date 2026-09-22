using System;
using System.Collections.Generic;
using System.Linq;
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

            bool isResponsive = s.MaxTokens == 600 && s.MaxConcurrentRequests == 3 && s.RequestTimeoutMs == 25000 && s.DefaultModCooldownTicks == 15 * 60;
            bool isBalanced = s.MaxTokens == 800 && s.MaxConcurrentRequests == 2 && s.RequestTimeoutMs == 45000 && s.DefaultModCooldownTicks == 30 * 60;
            bool isEco = s.MaxTokens == 400 && s.MaxConcurrentRequests == 1 && s.RequestTimeoutMs == 60000 && s.DefaultModCooldownTicks == 60 * 60;

            // Preset 1: High Responsive
            Rect btn1 = new Rect(barRect.x, barRect.y, btnW, barRect.height);
            string label1 = "RimMind.Settings.Preset.Responsive".Translate();
            if (isResponsive)
            {
                Widgets.DrawAtlas(btn1, Widgets.ButtonBGAtlasClick);
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btn1, label1);
                Text.Anchor = prevAnchor;
                if (Widgets.ButtonInvisible(btn1))
                {
                    ApplyPresetResponsive(s);
                }
            }
            else
            {
                if (Widgets.ButtonText(btn1, label1))
                {
                    ApplyPresetResponsive(s);
                }
            }
            TooltipHandler.TipRegion(btn1, "RimMind.Settings.Preset.Responsive.Desc".Translate());

            // Preset 2: Balanced
            Rect btn2 = new Rect(btn1.xMax + gap, barRect.y, btnW, barRect.height);
            string label2 = "RimMind.Settings.Preset.Balanced".Translate();
            if (isBalanced)
            {
                Widgets.DrawAtlas(btn2, Widgets.ButtonBGAtlasClick);
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btn2, label2);
                Text.Anchor = prevAnchor;
                if (Widgets.ButtonInvisible(btn2))
                {
                    ApplyPresetBalanced(s);
                }
            }
            else
            {
                if (Widgets.ButtonText(btn2, label2))
                {
                    ApplyPresetBalanced(s);
                }
            }
            TooltipHandler.TipRegion(btn2, "RimMind.Settings.Preset.Balanced.Desc".Translate());

            // Preset 3: Eco
            Rect btn3 = new Rect(btn2.xMax + gap, barRect.y, btnW, barRect.height);
            string label3 = "RimMind.Settings.Preset.Eco".Translate();
            if (isEco)
            {
                Widgets.DrawAtlas(btn3, Widgets.ButtonBGAtlasClick);
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btn3, label3);
                Text.Anchor = prevAnchor;
                if (Widgets.ButtonInvisible(btn3))
                {
                    ApplyPresetEco(s);
                }
            }
            else
            {
                if (Widgets.ButtonText(btn3, label3))
                {
                    ApplyPresetEco(s);
                }
            }
            TooltipHandler.TipRegion(btn3, "RimMind.Settings.Preset.Eco.Desc".Translate());

            listing.Gap(6f);
        }

        internal static void ApplyPresetResponsive(ISettingsProvider s)
        {
            s.MaxTokens = 600;
            s.MaxConcurrentRequests = 3;
            s.RequestTimeoutMs = 25000;
            s.DefaultModCooldownTicks = 15 * 60;
            s.Persist();
        }

        internal static void ApplyPresetBalanced(ISettingsProvider s)
        {
            s.MaxTokens = 800;
            s.MaxConcurrentRequests = 2;
            s.RequestTimeoutMs = 45000;
            s.DefaultModCooldownTicks = 30 * 60;
            s.Persist();
        }

        internal static void ApplyPresetEco(ISettingsProvider s)
        {
            s.MaxTokens = 400;
            s.MaxConcurrentRequests = 1;
            s.RequestTimeoutMs = 60000;
            s.DefaultModCooldownTicks = 60 * 60;
            s.Persist();
        }

        internal static string? CurrentPresetFeedback => null;
        private static void DrawConnectionSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            IExtensionRegistry<IAIClientFactory> providerRegistry,
            IPlayer2Lifecycle? player2Lifecycle,
            RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Connection".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Connection");
            listing.LabelWithTooltip("RimMind.Settings.Provider".Translate(), "RimMind.Settings.Provider.Desc".Translate());

            Rect row = listing.GetRect(28f);
            if (Widgets.ButtonText(row, GetActiveProviderDisplayLabel(s, providerRegistry)))
            {
                OpenProviderSelectionMenu(s, providerRegistry, player2Lifecycle);
            }
            TooltipHandler.TipRegion(row, "RimMind.Settings.Provider.Desc".Translate() + "\n\n" + "RimMind.Settings.Provider.SelectTip".Translate());

            listing.Gap(6f);

            if (s.Provider == "extended_service")
            {
                DrawExtendedServiceSection(listing, s, providerRegistry, scope);
            }
            else if (AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry))
            {
                DrawApiKeySection(listing, s, scope);
            }
            else
            {
                DrawPlayer2Section(listing, s, player2Lifecycle, scope);
            }

            listing.Gap(10f);

            DrawConnectionTestButton(listing, s, runtimeScope, providerRegistry);
        }

        internal static string GetActiveProviderDisplayLabel(
            ISettingsProvider s,
            IExtensionRegistry<IAIClientFactory>? providerRegistry = null)
        {
            if (s.Provider == "player2")
                return "RimMind.Settings.Provider.Player2".Translate();

            if (providerRegistry != null)
            {
                var factory = providerRegistry.All.FirstOrDefault(f => f.ProviderId == s.Provider);
                if (factory != null && !string.IsNullOrEmpty(factory.DisplayLabel))
                    return factory.DisplayLabel;
            }

            if (s.Provider == "openai")
            {
                string ep = (s.ApiEndpoint ?? string.Empty).ToLowerInvariant();
                if (ep.Contains("deepseek.com"))
                    return "DeepSeek (" + "RimMind.Settings.Provider.OpenAI".Translate() + ")";
                if (ep.Contains("siliconflow.cn"))
                    return "SiliconFlow (" + "RimMind.Settings.Provider.OpenAI".Translate() + ")";
                if (ep.Contains("moonshot.cn"))
                    return "Moonshot (" + "RimMind.Settings.Provider.OpenAI".Translate() + ")";
                if (ep.Contains("localhost:11434") || ep.Contains("127.0.0.1:11434"))
                    return "Ollama (" + "RimMind.Settings.Provider.OpenAI".Translate() + ")";
                if (ep.Contains("api.openai.com"))
                    return "RimMind.Settings.Provider.OpenAIOfficial".Translate();
                return "RimMind.Settings.Provider.OpenAI".Translate() + " (" + "RimMind.Settings.Provider.Custom".Translate() + ")";
            }

            return GetProviderLabel(s.Provider);
        }

        private static void OpenProviderSelectionMenu(
            ISettingsProvider s,
            IExtensionRegistry<IAIClientFactory>? providerRegistry,
            IPlayer2Lifecycle? player2Lifecycle)
        {
            var options = new List<FloatMenuOption>();

            // 1. OpenAI Compatible (OpenAI / 兼容)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.OpenAI".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.openai.com/v1", "gpt-4o-mini", "RimMind.Settings.Provider.OpenAI".Translate());
            }));

            // 2. DeepSeek (标准 OpenAI 兼容端点)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.DeepSeek".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.deepseek.com/v1", "deepseek-chat", "DeepSeek");
            }));

            // 3. Player2 (本地/云端)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.Player2".Translate(), () =>
            {
                s.Provider = "player2";
                s.Persist();
                RuntimeServiceScope operationScope = RuntimeServiceHub.Shared.Capture();
                var currentClientManager = ClientManager.ResolveOptional(operationScope);
                currentClientManager?.InvalidateCache();
            }));

            // 4+. 外部动态注入的服务商工厂 (由 providerRegistry 提供，如 ModelService 注入的 OpenCode Go, Extended Service 等)
            if (providerRegistry != null)
            {
                var externalFactories = providerRegistry.All
                    .Where(f => f.VisibleInMenu && f.ProviderId != "openai" && f.ProviderId != "player2")
                    .OrderBy(f => f.OrderWeight);

                foreach (var factory in externalFactories)
                {
                    string label = !string.IsNullOrEmpty(factory.DisplayLabel) ? factory.DisplayLabel : GetProviderLabel(factory.ProviderId);
                    var capturedFactory = factory;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        s.Provider = capturedFactory.ProviderId;
                        if (!string.IsNullOrEmpty(capturedFactory.DefaultEndpoint))
                            s.ApiEndpoint = capturedFactory.DefaultEndpoint!;
                        if (!string.IsNullOrEmpty(capturedFactory.DefaultModelName))
                            s.ModelName = capturedFactory.DefaultModelName!;
                        s.Persist();
                        RuntimeServiceScope operationScope = RuntimeServiceHub.Shared.Capture();
                        var currentClientManager = ClientManager.ResolveOptional(operationScope);
                        currentClientManager?.InvalidateCache();
                    }));
                }
            }

            var allProviders = AIProviderRegistry.GetAllProviderIds(providerRegistry);
            foreach (var p in allProviders)
            {
                if (p == "openai" || p == "player2" || (providerRegistry != null && providerRegistry.All.Any(f => f.ProviderId == p)))
                    continue;
                var label = GetProviderLabel(p);
                options.Add(new FloatMenuOption(label, () =>
                {
                    SwitchToProvider(s, p, player2Lifecycle);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal static void ApplyProviderPreset(
            ISettingsProvider s,
            string providerId,
            string endpoint,
            string modelName,
            string? presetName = null)
        {
            RuntimeServiceScope operationScope = RuntimeServiceHub.Shared.Capture();
            var currentSettings = SettingsProvider.Resolve(operationScope);
            var currentClientManager = ClientManager.ResolveOptional(operationScope);

            currentSettings.Provider = providerId;
            if (!string.IsNullOrEmpty(endpoint))
                currentSettings.ApiEndpoint = endpoint;
            if (!string.IsNullOrEmpty(modelName))
                currentSettings.ModelName = modelName;
            currentSettings.Persist();
            currentClientManager?.InvalidateCache();
        }

        internal static void SwitchToProvider(
            ISettingsProvider s,
            string providerId,
            IPlayer2Lifecycle? player2Lifecycle)
        {
            RuntimeServiceScope operationScope = RuntimeServiceHub.Shared.Capture();
            var currentSettings = SettingsProvider.Resolve(operationScope);
            var currentRegistry = ProviderRegistry.Resolve(operationScope);
            var currentPlayer2Lifecycle = player2Lifecycle ?? Player2Lifecycle.ResolveOptional(operationScope);
            var currentClientManager = ClientManager.ResolveOptional(operationScope);
            var prev = currentSettings.Provider;
            currentSettings.Provider = providerId;
            currentSettings.Persist();
            if (!AIProviderRegistry.RequiresApiKey(providerId, currentRegistry))
                currentPlayer2Lifecycle?.CheckStatusAndNotify();
            if (prev != providerId)
                currentClientManager?.InvalidateCache();
        }
        private static void DrawExtendedServiceSection(
            Listing_Standard listing,
            ISettingsProvider s,
            IExtensionRegistry<IAIClientFactory>? providerRegistry,
            RimMindLayoutScope? scope = null)
        {
            listing.LabelWithTooltip(
                "RimMind.Settings.ModelService.ActiveTitle".Translate(),
                "RimMind.Settings.ModelService.ActiveDesc".Translate(),
                new Color(0.5f, 0.9f, 0.6f));
            listing.Gap(4f);

            bool isRegistered = providerRegistry?.FindById("extended_service") != null;
            if (isRegistered)
            {
                Rect infoCard = listing.GetRect(32f);
                Widgets.DrawBoxSolid(infoCard, new Color(0.12f, 0.16f, 0.22f, 0.7f));
                Widgets.DrawHighlightIfMouseover(infoCard);
                TooltipHandler.TipRegion(infoCard, "RimMind.Settings.ModelService.ActiveDesc".Translate());

                Rect text1 = new Rect(infoCard.x + 8f, infoCard.y + 6f, infoCard.width - 16f, 20f);
                Widgets.Label(text1, "RimMind.Settings.ModelService.ActiveDesc".Translate());

                listing.Gap(6f);
                Rect btnRow = listing.GetRect(28f);
                if (Widgets.ButtonText(btnRow, "RimMind.Settings.ModelService.OpenTab".Translate()))
                {
                    RimMindCoreSettingsUI.CurrentTab = "model_service";
                }
                TooltipHandler.TipRegion(btnRow, "RimMind.Settings.ModelService.OpenTab.Desc".Translate());
            }
            else
            {
                listing.LabelWithTooltip(
                    "RimMind.Settings.Provider.ExtendedServiceNotInstalled".Translate(),
                    "RimMind.Settings.Provider.ExtendedServiceNotInstalled".Translate(),
                    new Color(1f, 0.7f, 0.3f));
            }
        }

        private static void DrawGenerationSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Generation".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Generation");

            listing.LabelWithTooltip($"{"RimMind.Settings.MaxTokens".Translate()}: {s.MaxTokens}", "RimMind.Settings.MaxTokens.Desc".Translate());
            s.MaxTokens = (int)listing.SliderWithTooltip(s.MaxTokens, 200f, 2000f, "RimMind.Settings.MaxTokens.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.Temperature".Translate()}: {s.DefaultTemperature:F2}", "RimMind.Settings.Temperature.Desc".Translate());
            s.DefaultTemperature = listing.SliderWithTooltip(s.DefaultTemperature, 0f, 2f, "RimMind.Settings.Temperature.Desc".Translate());

            listing.Gap(4f);
            var forceJsonMode = s.ForceJsonMode;
            listing.CheckboxLabeled(
                "RimMind.Settings.ForceJsonMode".Translate(),
                ref forceJsonMode,
                "RimMind.Settings.ForceJsonModeDesc".Translate());
            s.ForceJsonMode = forceJsonMode;

            listing.Gap(6f);
            listing.LabelWithTooltip("RimMind.UI.FlywheelAutoApply".Translate(), "RimMind.UI.FlywheelAutoApply.Desc".Translate());
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
                TooltipHandler.TipRegion(row, "RimMind.UI.FlywheelAutoApply.Desc".Translate());
            }

            listing.LabelWithTooltip("RimMind.UI.FlywheelConfidence".Translate(s.AutoApplyConfidenceThreshold), "RimMind.UI.FlywheelConfidence.Desc".Translate());
            s.AutoApplyConfidenceThreshold = listing.SliderWithTooltip(s.AutoApplyConfidenceThreshold, 0.5f, 1.0f, "RimMind.UI.FlywheelConfidence.Desc".Translate());
        }

        private static void DrawPerformanceSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Performance".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Performance");

            listing.LabelWithTooltip($"{"RimMind.Settings.MaxConcurrent".Translate()}: {s.MaxConcurrentRequests}", "RimMind.Settings.MaxConcurrent.Desc".Translate());
            s.MaxConcurrentRequests = (int)listing.SliderWithTooltip(s.MaxConcurrentRequests, 1f, 10f, "RimMind.Settings.MaxConcurrent.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.MaxRetry".Translate()}: {s.MaxRetryCount}", "RimMind.Settings.MaxRetry.Desc".Translate());
            s.MaxRetryCount = (int)listing.SliderWithTooltip(s.MaxRetryCount, 0f, 5f, "RimMind.Settings.MaxRetry.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.RequestTimeout".Translate()}: {s.RequestTimeoutMs / 1000}s", "RimMind.Settings.RequestTimeout.Desc".Translate());
            s.RequestTimeoutMs = (int)listing.SliderWithTooltip(s.RequestTimeoutMs / 1000f, 10f, 300f, "RimMind.Settings.RequestTimeout.Desc".Translate()) * 1000;

            listing.LabelWithTooltip($"{"RimMind.Settings.RequestExpireTicks".Translate()}: {s.RequestExpireTicks / 60f:F0}s ({s.RequestExpireTicks} ticks)", "RimMind.Settings.RequestExpireTicks.Desc".Translate());
            s.RequestExpireTicks = (int)listing.SliderWithTooltip(s.RequestExpireTicks, 6000f, 120000f, "RimMind.Settings.RequestExpireTicks.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.BehaviorHistoryMax".Translate()}: {s.BehaviorHistoryMax}", "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());
            s.BehaviorHistoryMax = (int)listing.SliderWithTooltip(s.BehaviorHistoryMax, 10f, 500f, "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.QueueProcessInterval".Translate()}: {s.QueueProcessInterval} ticks ({s.QueueProcessInterval / 60f:F1}s)", "RimMind.Settings.QueueProcessInterval.Desc".Translate());
            s.QueueProcessInterval = (int)listing.SliderWithTooltip(s.QueueProcessInterval, 10f, 300f, "RimMind.Settings.QueueProcessInterval.Desc".Translate());

            listing.LabelWithTooltip($"{"RimMind.Settings.DefaultModCooldown".Translate()}: {s.DefaultModCooldownTicks / 60f:F0}s ({s.DefaultModCooldownTicks} ticks)", "RimMind.Settings.DefaultModCooldown.Desc".Translate());
            s.DefaultModCooldownTicks = (int)listing.SliderWithTooltip(s.DefaultModCooldownTicks, 600f, 36000f, "RimMind.Settings.DefaultModCooldown.Desc".Translate());

            var queue = RequestQueue.ResolveOptional(runtimeScope);
            if (queue != null)
            {
                listing.Gap(4f);
                listing.LabelWithTooltip("RimMind.Settings.QueueSeeTab".Translate(), "RimMind.Settings.QueueSeeTab".Translate(), Color.gray);
            }
        }

        private static void DrawInterfaceSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Interface".Translate());
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
            listing.LabelWithTooltip("RimMind.Settings.ApiKey".Translate(), "RimMind.Settings.ApiKey.Desc".Translate());
            {
                Rect row = listing.GetRect(26f);
                float btnW = 52f;
                Rect field = new Rect(row.x, row.y, row.width - btnW - 4f, row.height);
                Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

                Widgets.DrawBoxSolid(field, new Color(0.04f, 0.05f, 0.08f, 0.6f));
                GUI.color = new Color(0.28f, 0.35f, 0.45f, 0.7f);
                Widgets.DrawBox(field, 1);
                GUI.color = Color.white;

                if (_showApiKey)
                {
                    s.ApiKey = Widgets.TextField(field, s.ApiKey ?? string.Empty);
                }
                else
                {
                    if (string.IsNullOrEmpty(s.ApiKey))
                    {
                        GUI.color = new Color(0.6f, 0.65f, 0.7f, 0.65f);
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), "RimMind.Settings.ApiKey.EmptyPlaceholder".Translate());
                        GUI.color = Color.white;
                    }
                    else
                    {
                        string hiddenLabel = "RimMind.Settings.ApiKey.Saved".Translate(s.ApiKey.Length).ToString();
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), hiddenLabel);
                    }

                    if (Widgets.ButtonInvisible(field))
                    {
                        _showApiKey = true;
                    }
                }
                TooltipHandler.TipRegion(field, "RimMind.Settings.ApiKey.Desc".Translate());
                if (Widgets.ButtonText(toggle, _showApiKey ? "RimMind.Settings.Hide".Translate() : "RimMind.Settings.Show".Translate()))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            listing.LabelWithTooltip("RimMind.Settings.ApiEndpoint".Translate(), "RimMind.Settings.ApiEndpoint.Desc".Translate());
            s.ApiEndpoint = listing.TextEntryWithTooltip(s.ApiEndpoint, "RimMind.Settings.ApiEndpoint.Desc".Translate());

            listing.Gap(4f);
            listing.LabelWithTooltip("RimMind.Settings.ModelName".Translate(), "RimMind.Settings.ModelName.Desc".Translate());
            s.ModelName = listing.TextEntryWithTooltip(s.ModelName, "RimMind.Settings.ModelName.Desc".Translate());
        }

        private static void DrawPlayer2Section(
            Listing_Standard listing,
            ISettingsProvider s,
            IPlayer2Lifecycle? player2Lifecycle,
            RimMindLayoutScope? scope = null)
        {
            listing.LabelWithTooltip("RimMind.Settings.Provider.Player2".Translate(), "RimMind.Settings.Player2.Desc".Translate());
            listing.Gap(4f);

            listing.LabelWithTooltip("RimMind.Settings.ApiKey".Translate() + " (" + "RimMind.Settings.Player2.ApiKeyOptional".Translate() + ")", "RimMind.Settings.Player2.ApiKeyDesc".Translate());
            {
                Rect row = listing.GetRect(26f);
                float btnW = 52f;
                Rect field = new Rect(row.x, row.y, row.width - btnW - 4f, row.height);
                Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

                Widgets.DrawBoxSolid(field, new Color(0.04f, 0.05f, 0.08f, 0.6f));
                GUI.color = new Color(0.28f, 0.35f, 0.45f, 0.7f);
                Widgets.DrawBox(field, 1);
                GUI.color = Color.white;

                if (_showApiKey)
                {
                    s.ApiKey = Widgets.TextField(field, s.ApiKey ?? string.Empty);
                }
                else
                {
                    if (string.IsNullOrEmpty(s.ApiKey))
                    {
                        GUI.color = new Color(0.6f, 0.65f, 0.7f, 0.65f);
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), "RimMind.Settings.ApiKey.EmptyPlaceholder".Translate());
                        GUI.color = Color.white;
                    }
                    else
                    {
                        string hiddenLabel = "RimMind.Settings.ApiKey.Saved".Translate(s.ApiKey.Length).ToString();
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), hiddenLabel);
                    }

                    if (Widgets.ButtonInvisible(field))
                    {
                        _showApiKey = true;
                    }
                }
                TooltipHandler.TipRegion(field, "RimMind.Settings.Player2.ApiKeyDesc".Translate());
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
            listing.LabelWithTooltip("RimMind.Settings.Player2.RemoteUrl".Translate(), "RimMind.Settings.Player2.RemoteUrl.Desc".Translate());
            s.Player2RemoteUrl = listing.TextEntryWithTooltip(s.Player2RemoteUrl, "RimMind.Settings.Player2.RemoteUrl.Desc".Translate());

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
