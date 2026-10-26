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
        private static int _lastDrawnFrame = -1;
        private static float _lastMeasuredHeight;
        private static string _testStatus = "";
        private static Color _testStatusColor = Color.white;
        private static bool _testPending;
        private static Vector2 _apiScroll;
        private static readonly GenerationUiState GenerationState = new GenerationUiState();

        internal static bool IsShowingApiKey => _showApiKey;
        internal static int EditingBackupKeyIndex => _editingBackupKeyIndex;

        public static void ResetSecretVisibility()
        {
            _showApiKey = false;
            _editingBackupKeyIndex = -1;
        }

        internal static void SetSecretVisibilityForTest(bool showApiKey, int editingBackupIndex = -1)
        {
            _showApiKey = showApiKey;
            _editingBackupKeyIndex = editingBackupIndex;
        }

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
            int currentFrame = Time.frameCount;
            if (_lastDrawnFrame >= 0 && currentFrame > _lastDrawnFrame + 1)
            {
                ResetSecretVisibility();
            }
            _lastDrawnFrame = currentFrame;

            if (GenerationState.Refresh(runtimeScope.Generation))
            {
                ResetSecretVisibility();
                _lastMeasuredHeight = 0f;
                _activeConnectionTest = null;
                _testPending = false;
                _testStatus = string.Empty;
                _testStatusColor = Color.white;
                GenerationState.MarkDerivedState();
            }

            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 5, rowsPerSection: 4);
            float contentH = Mathf.Max(EstimateApiHeight(s), Mathf.Max(_lastMeasuredHeight, formLayout.ContentHeight));
            Rect viewRect = new Rect(0f, 0f, formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth, contentH);
            Widgets.BeginScrollView(inRect, ref _apiScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Api:Viewport");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Tab.Api"));

            DrawPresetsBar(listing, s);

            DrawConnectionSection(listing, s, runtimeScope, providerRegistry, player2Lifecycle, scope);
            DrawGenerationSection(listing, s, scope);
            DrawPerformanceSection(listing, s, runtimeScope, scope);
            DrawInterfaceSection(listing, s, scope);

            _lastMeasuredHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPresetsBar(Listing_Standard listing, ISettingsProvider s)
        {
            listing.Label(UiText.Get("RimMind.Settings.PresetsBarTitle"));
            Rect barRect = listing.GetRect(28f);
            float gap = 6f;
            float btnW = (barRect.width - gap * 2f) / 3f;

            bool isResponsive = s.MaxTokens == 600 && s.MaxConcurrentRequests == 3 && s.RequestTimeoutMs == 25000 && s.DefaultModCooldownTicks == 15 * 60 && Mathf.Abs(s.ActivityFrequencyScale - 2.0f) < 0.1f;
            bool isBalanced = s.MaxTokens == 800 && s.MaxConcurrentRequests == 2 && s.RequestTimeoutMs == 45000 && s.DefaultModCooldownTicks == 30 * 60 && Mathf.Abs(s.ActivityFrequencyScale - 1.0f) < 0.1f;
            bool isEco = s.MaxTokens == 400 && s.MaxConcurrentRequests == 1 && s.RequestTimeoutMs == 60000 && s.DefaultModCooldownTicks == 60 * 60 && Mathf.Abs(s.ActivityFrequencyScale - 0.33f) < 0.1f;

            // Preset 1: High Responsive
            Rect btn1 = new Rect(barRect.x, barRect.y, btnW, barRect.height);
            if (SettingsUIDrawer.DrawSelectableButton(
                btn1,
                UiText.Get("RimMind.Settings.Preset.Responsive"),
                isResponsive,
                UiText.Get("RimMind.Settings.Preset.Responsive.Desc")))
            {
                ApplyPresetResponsive(s);
            }

            // Preset 2: Balanced
            Rect btn2 = new Rect(btn1.xMax + gap, barRect.y, btnW, barRect.height);
            if (SettingsUIDrawer.DrawSelectableButton(
                btn2,
                UiText.Get("RimMind.Settings.Preset.Balanced"),
                isBalanced,
                UiText.Get("RimMind.Settings.Preset.Balanced.Desc")))
            {
                ApplyPresetBalanced(s);
            }

            // Preset 3: Eco
            Rect btn3 = new Rect(btn2.xMax + gap, barRect.y, btnW, barRect.height);
            if (SettingsUIDrawer.DrawSelectableButton(
                btn3,
                UiText.Get("RimMind.Settings.Preset.Eco"),
                isEco,
                UiText.Get("RimMind.Settings.Preset.Eco.Desc")))
            {
                ApplyPresetEco(s);
            }

            listing.Gap(6f);
        }

        internal static void ApplyPresetResponsive(ISettingsProvider s)
        {
            s.MaxTokens = 600;
            s.MaxConcurrentRequests = 3;
            s.RequestTimeoutMs = 25000;
            s.DefaultModCooldownTicks = 15 * 60;
            s.ActivityFrequencyScale = 2.0f;
            s.Persist();
        }

        internal static void ApplyPresetBalanced(ISettingsProvider s)
        {
            s.MaxTokens = 800;
            s.MaxConcurrentRequests = 2;
            s.RequestTimeoutMs = 45000;
            s.DefaultModCooldownTicks = 30 * 60;
            s.ActivityFrequencyScale = 1.0f;
            s.Persist();
        }

        internal static void ApplyPresetEco(ISettingsProvider s)
        {
            s.MaxTokens = 400;
            s.MaxConcurrentRequests = 1;
            s.RequestTimeoutMs = 60000;
            s.DefaultModCooldownTicks = 60 * 60;
            s.ActivityFrequencyScale = 0.33f;
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
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Section.Connection"));
            scope?.Record(listing.GetRect(0f), "Section:Connection");
            string providerDesc = UiText.Get("RimMind.Settings.Provider.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.Provider"), providerDesc);

            Rect row = listing.GetRect(28f);
            if (Widgets.ButtonText(row, GetActiveProviderDisplayLabel(s, providerRegistry)))
            {
                OpenProviderSelectionMenu(s, providerRegistry, player2Lifecycle);
            }
            TooltipHandler.TipRegion(row, providerDesc + "\n\n" + UiText.Get("RimMind.Settings.Provider.SelectTip"));

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
                return UiText.Get("RimMind.Settings.Provider.Player2");

            if (providerRegistry != null)
            {
                var factory = providerRegistry.All.FirstOrDefault(f => f.ProviderId == s.Provider);
                if (factory != null && !string.IsNullOrEmpty(factory.DisplayLabel))
                    return factory.DisplayLabel;
            }

            if (s.Provider == "openai")
            {
                string ep = (s.ApiEndpoint ?? string.Empty).ToLowerInvariant();
                string openAiLabel = UiText.Get("RimMind.Settings.Provider.OpenAI");
                if (ep.Contains("deepseek.com"))
                    return "DeepSeek (" + openAiLabel + ")";
                if (ep.Contains("siliconflow.cn"))
                    return "SiliconFlow (" + openAiLabel + ")";
                if (ep.Contains("moonshot.cn"))
                    return "Moonshot (" + openAiLabel + ")";
                if (ep.Contains("localhost:11434") || ep.Contains("127.0.0.1:11434"))
                    return "Ollama (" + openAiLabel + ")";
                if (ep.Contains("api.openai.com"))
                    return UiText.Get("RimMind.Settings.Provider.OpenAIOfficial");
                return openAiLabel + " (" + UiText.Get("RimMind.Settings.Provider.Custom") + ")";
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
            string openAiLabel = UiText.Get("RimMind.Settings.Provider.OpenAI");
            options.Add(new FloatMenuOption(openAiLabel, () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.openai.com/v1", "gpt-4o-mini", UiText.Get("RimMind.Settings.Provider.OpenAI"));
            }));

            // 2. DeepSeek (标准 OpenAI 兼容端点)
            options.Add(new FloatMenuOption(UiText.Get("RimMind.Settings.Provider.DeepSeek"), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.deepseek.com/v1", "deepseek-chat", "DeepSeek");
            }));

            // 3. Player2 (本地/云端)
            options.Add(new FloatMenuOption(UiText.Get("RimMind.Settings.Provider.Player2"), () =>
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
                UiText.Get("RimMind.Settings.ModelService.ActiveTitle"),
                UiText.Get("RimMind.Settings.ModelService.ActiveDesc"),
                new Color(0.5f, 0.9f, 0.6f));
            listing.Gap(4f);

            bool isRegistered = providerRegistry?.FindById("extended_service") != null;
            if (isRegistered)
            {
                Rect btnRow = listing.GetRect(28f);
                if (Widgets.ButtonText(btnRow, UiText.Get("RimMind.Settings.ModelService.OpenTab")))
                {
                    RimMindCoreSettingsUI.CurrentTab = "model_service";
                }
                TooltipHandler.TipRegion(btnRow, UiText.Get("RimMind.Settings.ModelService.OpenTab.Desc"));
            }
            else
            {
                string notInstalled = UiText.Get("RimMind.Settings.Provider.ExtendedServiceNotInstalled");
                listing.LabelWithTooltip(
                    notInstalled,
                    notInstalled,
                    new Color(1f, 0.7f, 0.3f));
            }
        }

        private static void DrawGenerationSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Section.Generation"));
            scope?.Record(listing.GetRect(0f), "Section:Generation");

            string freqDesc = GetActivityFrequencyDescription(s.ActivityFrequencyScale);
            string freqTooltip = UiText.Get("RimMind.Settings.ActivityFrequencyScale.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.ActivityFrequencyScale")}: {freqDesc} ({s.ActivityFrequencyScale:F1}x)", freqTooltip);
            s.ActivityFrequencyScale = listing.SliderWithTooltip(s.ActivityFrequencyScale, 0.1f, 3.5f, freqTooltip);
            listing.Gap(4f);

            string maxTokensDesc = UiText.Get("RimMind.Settings.MaxTokens.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.MaxTokens")}: {s.MaxTokens}", maxTokensDesc);
            s.MaxTokens = (int)listing.SliderWithTooltip(s.MaxTokens, 200f, 2000f, maxTokensDesc);

            string tempDesc = UiText.Get("RimMind.Settings.Temperature.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.Temperature")}: {s.DefaultTemperature:F2}", tempDesc);
            s.DefaultTemperature = listing.SliderWithTooltip(s.DefaultTemperature, 0f, 2f, tempDesc);

            listing.Gap(4f);
            var forceJsonMode = s.ForceJsonMode;
            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.ForceJsonMode"),
                ref forceJsonMode,
                UiText.Get("RimMind.Settings.ForceJsonModeDesc"));
            s.ForceJsonMode = forceJsonMode;

            listing.Gap(6f);
            string autoApplyDesc = UiText.Get("RimMind.UI.FlywheelAutoApply.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.UI.FlywheelAutoApply"), autoApplyDesc);
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
                TooltipHandler.TipRegion(row, autoApplyDesc);
            }

            string confidenceDesc = UiText.Get("RimMind.UI.FlywheelConfidence.Desc");
            listing.LabelWithTooltip(UiText.Format("RimMind.UI.FlywheelConfidence", s.AutoApplyConfidenceThreshold), confidenceDesc);
            s.AutoApplyConfidenceThreshold = listing.SliderWithTooltip(s.AutoApplyConfidenceThreshold, 0.5f, 1.0f, confidenceDesc);
        }

        private static void DrawPerformanceSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            _ = runtimeScope;
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                UiText.Get("RimMind.Settings.Section.Performance"),
                UiText.Get("RimMind.Settings.QueueSeeTab", "Queue status has been moved to the \"Queue\" tab"));

            string maxConcurrentDesc = UiText.Get("RimMind.Settings.MaxConcurrent.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.MaxConcurrent")}: {s.MaxConcurrentRequests}", maxConcurrentDesc);
            s.MaxConcurrentRequests = (int)listing.SliderWithTooltip(s.MaxConcurrentRequests, 1f, 10f, maxConcurrentDesc);

            string maxRetryDesc = UiText.Get("RimMind.Settings.MaxRetry.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.MaxRetry")}: {s.MaxRetryCount}", maxRetryDesc);
            s.MaxRetryCount = (int)listing.SliderWithTooltip(s.MaxRetryCount, 0f, 5f, maxRetryDesc);

            string timeoutDesc = UiText.Get("RimMind.Settings.RequestTimeout.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.RequestTimeout")}: {s.RequestTimeoutMs / 1000}s", timeoutDesc);
            s.RequestTimeoutMs = (int)listing.SliderWithTooltip(s.RequestTimeoutMs / 1000f, 10f, 300f, timeoutDesc) * 1000;

            string expireDesc = UiText.Get("RimMind.Settings.RequestExpireTicks.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.RequestExpireTicks")}: {s.RequestExpireTicks / 60f:F0}s ({s.RequestExpireTicks} ticks)", expireDesc);
            s.RequestExpireTicks = (int)listing.SliderWithTooltip(s.RequestExpireTicks, 6000f, 120000f, expireDesc);

            string historyMaxDesc = UiText.Get("RimMind.Settings.BehaviorHistoryMax.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.BehaviorHistoryMax")}: {s.BehaviorHistoryMax}", historyMaxDesc);
            s.BehaviorHistoryMax = (int)listing.SliderWithTooltip(s.BehaviorHistoryMax, 10f, 500f, historyMaxDesc);

            string queueIntervalDesc = UiText.Get("RimMind.Settings.QueueProcessInterval.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.QueueProcessInterval")}: {s.QueueProcessInterval} ticks ({s.QueueProcessInterval / 60f:F1}s)", queueIntervalDesc);
            s.QueueProcessInterval = (int)listing.SliderWithTooltip(s.QueueProcessInterval, 10f, 300f, queueIntervalDesc);

            string cooldownDesc = UiText.Get("RimMind.Settings.DefaultModCooldown.Desc");
            listing.LabelWithTooltip($"{UiText.Get("RimMind.Settings.DefaultModCooldown")}: {s.DefaultModCooldownTicks / 60f:F0}s ({s.DefaultModCooldownTicks} ticks)", cooldownDesc);
            s.DefaultModCooldownTicks = (int)listing.SliderWithTooltip(s.DefaultModCooldownTicks, 600f, 36000f, cooldownDesc);
        }

        private static void DrawInterfaceSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.Section.Interface"));
            scope?.Record(listing.GetRect(0f), "Section:Interface");

            var autoHide = s.RequestOverlayAutoHideWhenEmpty;
            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RequestOverlayAutoHideWhenEmpty"),
                ref autoHide,
                UiText.Get("RimMind.Settings.RequestOverlayAutoHideWhenEmpty.Desc"));
            s.RequestOverlayAutoHideWhenEmpty = autoHide;

            var showProgress = s.ShowAgentProgressFloat;
            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.ShowAgentProgressFloat"),
                ref showProgress,
                UiText.Get("RimMind.Settings.ShowAgentProgressFloat.Desc"));
            s.ShowAgentProgressFloat = showProgress;

            var mentalMonitor = s.EnableFloatingMentalMonitor;
            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.EnableFloatingMentalMonitor"),
                ref mentalMonitor,
                UiText.Get("RimMind.Settings.EnableFloatingMentalMonitor.Desc"));
            s.EnableFloatingMentalMonitor = mentalMonitor;

            listing.Gap(6f);
            Rect inspectorBtnRect = listing.GetRect(30f);
            if (Widgets.ButtonText(inspectorBtnRect, UiText.Get("RimMind.Settings.OpenContextPayloadInspector")))
            {
                Find.WindowStack.Add(new Window_ContextPayloadInspector());
            }
            TooltipHandler.TipRegion(inspectorBtnRect, UiText.Get("RimMind.Settings.OpenContextPayloadInspector.Desc"));

            listing.Gap(6f);
            var debugLogging = s.DebugLogging;
            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.DebugLogging"),
                ref debugLogging,
                UiText.Get("RimMind.Settings.DebugLogging.Desc"));
            s.DebugLogging = debugLogging;
        }

        private static void DrawApiKeySection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            string apiKeyDesc = UiText.Get("RimMind.Settings.ApiKey.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.ApiKey"), apiKeyDesc);
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
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), UiText.Get("RimMind.Settings.ApiKey.EmptyPlaceholder"));
                        GUI.color = Color.white;
                    }
                    else
                    {
                        string hiddenLabel = UiText.Format("RimMind.Settings.ApiKey.Saved", s.ApiKey.Length);
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), hiddenLabel);
                    }

                    if (Widgets.ButtonInvisible(field))
                    {
                        _showApiKey = true;
                    }
                }
                TooltipHandler.TipRegion(field, apiKeyDesc);
                if (Widgets.ButtonText(toggle, _showApiKey ? UiText.Get("RimMind.Settings.Hide") : UiText.Get("RimMind.Settings.Show")))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            string endpointDesc = UiText.Get("RimMind.Settings.ApiEndpoint.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.ApiEndpoint"), endpointDesc);
            s.ApiEndpoint = listing.TextEntryWithTooltip(s.ApiEndpoint, endpointDesc);

            listing.Gap(4f);
            string modelDesc = UiText.Get("RimMind.Settings.ModelName.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.ModelName"), modelDesc);
            s.ModelName = listing.TextEntryWithTooltip(s.ModelName, modelDesc);

            listing.Gap(8f);
            DrawBackupEndpointsSection(listing, s);
        }

        private static int _editingBackupKeyIndex = -1;

        private static void DrawBackupEndpointsSection(Listing_Standard listing, ISettingsProvider s)
        {
            var coreSettings = RimMindCoreMod.Settings;
            if (coreSettings == null) return;
            coreSettings.backupEndpoints ??= new List<BackupEndpointEntry>();
            var list = coreSettings.backupEndpoints;

            string headerLabel = UiText.Get("RimMind.Settings.BackupEndpoints", "Backup API Failover Nodes");
            string headerDesc = UiText.Get("RimMind.Settings.BackupEndpoints.Desc",
                "Ordered failover list of backup API endpoints. When the primary endpoint fails, RimMind automatically switches to the next healthy backup node.");

            Rect headerRow = listing.GetRect(26f);
            float btnW = 148f;
            Rect titleRect = new Rect(headerRow.x, headerRow.y + 2f, headerRow.width - (btnW * 2f + 12f), headerRow.height);
            Rect addOpenCodeRect = new Rect(headerRow.xMax - (btnW * 2f + 6f), headerRow.y, btnW, 24f);
            Rect addCustomRect = new Rect(headerRow.xMax - btnW, headerRow.y, btnW, 24f);

            Widgets.Label(titleRect, headerLabel + $" ({list.Count})");
            TooltipHandler.TipRegion(titleRect, headerDesc);

            if (Widgets.ButtonText(addOpenCodeRect, UiText.Get("RimMind.Settings.BackupEndpoints.AddOpenCode", "+ OpenCode Go Backup")))
            {
                list.Add(new BackupEndpointEntry
                {
                    name = $"OpenCode Go #{list.Count + 1}",
                    provider = "opencode",
                    apiEndpoint = "https://opencode.ai/zen/go/v1",
                    modelName = "deepseek-v4.1-flash",
                    apiKey = string.Empty,
                    enabled = true
                });
                s.Persist();
            }
            TooltipHandler.TipRegion(addOpenCodeRect, headerDesc);

            if (Widgets.ButtonText(addCustomRect, UiText.Get("RimMind.Settings.BackupEndpoints.AddCustom", "+ Custom Backup")))
            {
                list.Add(new BackupEndpointEntry
                {
                    name = $"Backup #{list.Count + 1}",
                    provider = "openai",
                    apiEndpoint = "https://api.deepseek.com/v1",
                    modelName = "deepseek-chat",
                    apiKey = string.Empty,
                    enabled = true
                });
                s.Persist();
            }
            TooltipHandler.TipRegion(addCustomRect, headerDesc);

            if (list.Count == 0)
            {
                Rect emptyRow = listing.GetRect(22f);
                GUI.color = new Color(0.6f, 0.65f, 0.72f, 0.8f);
                Widgets.Label(emptyRow, UiText.Get("RimMind.Settings.BackupEndpoints.Empty", "No backup nodes configured."));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(emptyRow, headerDesc);
                return;
            }

            int removeIdx = -1;
            int moveUpIdx = -1;
            int moveDownIdx = -1;

            string endpointDesc = UiText.Get("RimMind.Settings.ApiEndpoint.Desc");
            string modelDesc = UiText.Get("RimMind.Settings.ModelName.Desc");
            string apiKeyDesc = UiText.Get("RimMind.Settings.ApiKey.Desc");
            string hideLabel = UiText.Get("RimMind.Settings.Hide");
            string showLabel = UiText.Get("RimMind.Settings.Show");

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry == null) continue;

                listing.Gap(4f);
                Rect cardRow1 = listing.GetRect(24f);
                Widgets.DrawBoxSolid(cardRow1, new Color(0.08f, 0.10f, 0.14f, 0.55f));

                Rect enableRect = new Rect(cardRow1.x + 4f, cardRow1.y, 24f, 24f);
                Widgets.Checkbox(enableRect.x, enableRect.y, ref entry.enabled);

                Rect badgeRect = new Rect(enableRect.xMax + 4f, cardRow1.y + 2f, 34f, 20f);
                Widgets.Label(badgeRect, $"#{i + 1}");

                float actionBtnsW = 92f;
                float fieldGap = 6f;
                float remainingW = cardRow1.width - 70f - actionBtnsW - fieldGap * 3f;
                float endpointW = remainingW * 0.56f;
                float modelW = remainingW * 0.44f;

                Rect epRect = new Rect(badgeRect.xMax + 4f, cardRow1.y + 1f, endpointW, 22f);
                entry.apiEndpoint = Widgets.TextField(epRect, entry.apiEndpoint ?? string.Empty);
                TooltipHandler.TipRegion(epRect, endpointDesc);
                if (entry.apiEndpoint.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) >= 0)
                    entry.provider = "opencode";

                Rect modelRect = new Rect(epRect.xMax + fieldGap, cardRow1.y + 1f, modelW, 22f);
                entry.modelName = Widgets.TextField(modelRect, entry.modelName ?? string.Empty);
                TooltipHandler.TipRegion(modelRect, modelDesc);

                Rect upBtn = new Rect(cardRow1.xMax - actionBtnsW, cardRow1.y + 1f, 26f, 22f);
                Rect downBtn = new Rect(upBtn.xMax + 3f, cardRow1.y + 1f, 26f, 22f);
                Rect delBtn = new Rect(downBtn.xMax + 3f, cardRow1.y + 1f, 30f, 22f);

                if (i > 0 && Widgets.ButtonText(upBtn, "↑")) moveUpIdx = i;
                if (i < list.Count - 1 && Widgets.ButtonText(downBtn, "↓")) moveDownIdx = i;
                if (Widgets.ButtonText(delBtn, "×")) removeIdx = i;

                Rect cardRow2 = listing.GetRect(24f);
                float showBtnW = 52f;
                Rect keyField = new Rect(cardRow2.x + 32f, cardRow2.y + 1f, cardRow2.width - 32f - showBtnW - 4f, 22f);
                Rect showToggle = new Rect(keyField.xMax + 4f, cardRow2.y + 1f, showBtnW, 22f);

                bool showingThisKey = _editingBackupKeyIndex == i;
                if (showingThisKey)
                {
                    entry.apiKey = Widgets.TextField(keyField, entry.apiKey ?? string.Empty);
                }
                else
                {
                    Widgets.DrawBoxSolid(keyField, new Color(0.04f, 0.05f, 0.08f, 0.6f));
                    string keyPreview = string.IsNullOrEmpty(entry.apiKey)
                        ? UiText.Get("RimMind.Settings.ApiKey.EmptyPlaceholder")
                        : UiText.Format("RimMind.Settings.ApiKey.Saved", entry.apiKey.Length);
                    Widgets.Label(new Rect(keyField.x + 6f, keyField.y + 2f, keyField.width - 12f, keyField.height), keyPreview);
                    if (Widgets.ButtonInvisible(keyField))
                        _editingBackupKeyIndex = i;
                }
                TooltipHandler.TipRegion(keyField, apiKeyDesc);
                if (Widgets.ButtonText(showToggle, showingThisKey ? hideLabel : showLabel))
                {
                    _editingBackupKeyIndex = showingThisKey ? -1 : i;
                }
            }

            if (removeIdx >= 0 && removeIdx < list.Count)
            {
                list.RemoveAt(removeIdx);
                if (_editingBackupKeyIndex == removeIdx) _editingBackupKeyIndex = -1;
                s.Persist();
            }
            else if (moveUpIdx > 0 && moveUpIdx < list.Count)
            {
                var tmp = list[moveUpIdx - 1];
                list[moveUpIdx - 1] = list[moveUpIdx];
                list[moveUpIdx] = tmp;
                s.Persist();
            }
            else if (moveDownIdx >= 0 && moveDownIdx < list.Count - 1)
            {
                var tmp = list[moveDownIdx + 1];
                list[moveDownIdx + 1] = list[moveDownIdx];
                list[moveDownIdx] = tmp;
                s.Persist();
            }
        }

        private static void DrawPlayer2Section(
            Listing_Standard listing,
            ISettingsProvider s,
            IPlayer2Lifecycle? player2Lifecycle,
            RimMindLayoutScope? scope = null)
        {
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.Provider.Player2"), UiText.Get("RimMind.Settings.Player2.Desc"));
            listing.Gap(4f);

            string player2ApiKeyDesc = UiText.Get("RimMind.Settings.Player2.ApiKeyDesc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.ApiKey") + " (" + UiText.Get("RimMind.Settings.Player2.ApiKeyOptional") + ")", player2ApiKeyDesc);
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
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), UiText.Get("RimMind.Settings.ApiKey.EmptyPlaceholder"));
                        GUI.color = Color.white;
                    }
                    else
                    {
                        string hiddenLabel = UiText.Format("RimMind.Settings.ApiKey.Saved", s.ApiKey.Length);
                        Widgets.Label(new Rect(field.x + 8f, field.y + 3f, field.width - 16f, field.height), hiddenLabel);
                    }

                    if (Widgets.ButtonInvisible(field))
                    {
                        _showApiKey = true;
                    }
                }
                TooltipHandler.TipRegion(field, player2ApiKeyDesc);
                if (Widgets.ButtonText(toggle, _showApiKey ? UiText.Get("RimMind.Settings.Hide") : UiText.Get("RimMind.Settings.Show")))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            {
                Rect checkBtnRow = listing.GetRect(28f);
                if (Widgets.ButtonText(checkBtnRow, UiText.Get("RimMind.Settings.Player2.CheckLocal")))
                    player2Lifecycle?.CheckStatusAndNotify();
            }

            listing.Gap(4f);
            string remoteUrlDesc = UiText.Get("RimMind.Settings.Player2.RemoteUrl.Desc");
            listing.LabelWithTooltip(UiText.Get("RimMind.Settings.Player2.RemoteUrl"), remoteUrlDesc);
            s.Player2RemoteUrl = listing.TextEntryWithTooltip(s.Player2RemoteUrl, remoteUrlDesc);

            listing.Gap(4f);
            {
                float balance = player2Lifecycle?.CachedBalance ?? -1;
                string balanceText;
                if (balance >= 0)
                {
                    const string balanceKey = "RimMind.Settings.Player2.BalanceJoules";
                    string formatted = UiText.Format(balanceKey, balance.ToString("F2"));
                    balanceText = formatted == balanceKey ? $"Joules: {balance:F2}" : formatted;
                }
                else
                {
                    balanceText = UiText.Get("RimMind.Settings.Player2.BalanceUnknown", "Balance: unknown");
                }
                listing.Label(balanceText);

                Rect refreshRow = listing.GetRect(28f);
                if (Widgets.ButtonText(refreshRow, UiText.Get("RimMind.Settings.Player2.RefreshBalance")))
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
            bool testClicked = Widgets.ButtonText(btn, UiText.Get("RimMind.Settings.TestConnection"));
            GUI.enabled = wasEnabled;
            if (testClicked)
                RunConnectionTest(s, runtimeScope, providerRegistry);
            GUI.color = _testStatusColor;
            Widgets.Label(status, _testStatus);
            GUI.color = Color.white;
        }

        internal static string GetActivityFrequencyDescription(float scale)
        {
            if (scale <= 0.35f) return UiText.Get("RimMind.Settings.ActivityFrequency.Low");
            if (scale <= 0.75f) return UiText.Get("RimMind.Settings.ActivityFrequency.SubMedium");
            if (scale <= 1.4f) return UiText.Get("RimMind.Settings.ActivityFrequency.Medium");
            if (scale <= 2.4f) return UiText.Get("RimMind.Settings.ActivityFrequency.High");
            return UiText.Get("RimMind.Settings.ActivityFrequency.VeryHigh");
        }
    }
}
