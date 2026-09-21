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
            listing.LabelWithTooltip("RimMind.Settings.Provider".Translate(), "RimMind.Settings.Provider.Desc".Translate());

            // Row with Main Provider Display and Quick Presets Button
            {
                Rect row = listing.GetRect(28f);
                float presetBtnW = 144f;
                Rect provRect = new Rect(row.x, row.y, row.width - presetBtnW - 6f, row.height);
                Rect presetBtnRect = new Rect(provRect.xMax + 6f, row.y, presetBtnW, row.height);

                if (Widgets.ButtonText(provRect, GetActiveProviderDisplayLabel(s)))
                {
                    OpenProviderSelectionMenu(s, providerRegistry, player2Lifecycle);
                }
                TooltipHandler.TipRegion(provRect, "RimMind.Settings.Provider.SelectTip".Translate());
                TooltipHandler.TipRegion(provRect, "RimMind.Settings.Provider.Desc".Translate() + "\n\n" + "RimMind.Settings.Provider.SelectTip".Translate());

                if (Widgets.ButtonText(presetBtnRect, "RimMind.Settings.ProviderPresetBtn".Translate()))
                {
                    OpenProviderSelectionMenu(s, providerRegistry, player2Lifecycle);
                }
                TooltipHandler.TipRegion(presetBtnRect, "RimMind.Settings.ProviderPresetBtn.Desc".Translate());
            }

            listing.Gap(6f);

            // One-click quick button for OpenCode Go if not currently active
            if (!IsOpenCodeGoActive(s))
            {
                Rect opencodeBar = listing.GetRect(26f);
                if (Widgets.ButtonText(opencodeBar, "RimMind.Settings.QuickApplyOpenCodeGo".Translate()))
                {
                    ApplyProviderPreset(s, "openai", "https://opencode.ai/zen/go/v1", "deepseek-v4.1-flash", "OpenCode Go");
                }
                listing.Gap(4f);
            }

            if (s.Provider == "extended_service")
            {
                DrawExtendedServiceSection(listing, s, scope);
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

        internal static bool IsOpenCodeGoActive(ISettingsProvider s)
        {
            if (s.Provider == "openai")
            {
                string ep = s.ApiEndpoint ?? string.Empty;
                return ep.IndexOf("opencode.ai", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        internal static string GetActiveProviderDisplayLabel(ISettingsProvider s)
        {
            if (s.Provider == "player2")
                return "RimMind.Settings.Provider.Player2".Translate();

            if (s.Provider == "extended_service")
                return "RimMind.Settings.Provider.ExtendedService".Translate();

            if (s.Provider == "openai")
            {
                string ep = (s.ApiEndpoint ?? string.Empty).ToLowerInvariant();
                if (ep.Contains("opencode.ai"))
                    return "OpenCode Go (" + "RimMind.Settings.Provider.OpenAI".Translate() + ")";
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
            IExtensionRegistry<IAIClientFactory> providerRegistry,
            IPlayer2Lifecycle? player2Lifecycle)
        {
            var options = new List<FloatMenuOption>();

            // 1. OpenCode Go (订阅直连 / sub2api)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.OpenCodeGo".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://opencode.ai/zen/go/v1", "deepseek-v4.1-flash", "OpenCode Go");
            }));

            // 2. DeepSeek (深度求索)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.DeepSeek".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.deepseek.com/v1", "deepseek-chat", "DeepSeek");
            }));

            // 3. SiliconFlow (硅基流动)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.SiliconFlow".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.siliconflow.cn/v1", "deepseek-ai/DeepSeek-V3", "SiliconFlow");
            }));

            // 4. Moonshot AI (月之暗面 Kimi)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.Moonshot".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.moonshot.cn/v1", "moonshot-v1-8k", "Moonshot AI");
            }));

            // 5. Ollama (本地私有化部署)
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.Ollama".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "http://localhost:11434/v1", "llama3", "Ollama");
            }));

            // 6. OpenAI 官方
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.OpenAIOfficial".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", "https://api.openai.com/v1", "gpt-4o-mini", "OpenAI");
            }));

            // 7. 自定义 OpenAI 兼容端点
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.OpenAICustom".Translate(), () =>
            {
                ApplyProviderPreset(s, "openai", s.ApiEndpoint, s.ModelName, "OpenAI Compatible");
            }));

            // 8. 扩展模型服务 (ModelService)
            string extLabel = IsModelServiceInstalled()
                ? "RimMind.Settings.Provider.ExtendedService".Translate()
                : "RimMind.Settings.Provider.ExtendedServiceNotInstalled".Translate();
            options.Add(new FloatMenuOption(extLabel, () =>
            {
                SwitchToProvider(s, "extended_service", player2Lifecycle);
            }));

            // 9. Player2 桌面应用
            options.Add(new FloatMenuOption("RimMind.Settings.Provider.Player2".Translate(), () =>
            {
                SwitchToProvider(s, "player2", player2Lifecycle);
            }));

            // 10. Any other registered custom providers from other mods
            var allProviders = AIProviderRegistry.GetAllProviderIds(providerRegistry);
            foreach (var p in allProviders)
            {
                if (p == "openai" || p == "player2" || p == "extended_service")
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
            string presetName)
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

            string msg = "RimMind.Settings.ProviderApplied".Translate(presetName);
            _presetAppliedMessage = msg;
            _presetAppliedUntilTick = Environment.TickCount + 4000;
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

            string label = GetProviderLabel(providerId);
            _presetAppliedMessage = "RimMind.Settings.ProviderSwitched".Translate(label);
            _presetAppliedUntilTick = Environment.TickCount + 4000;
        }

        private static void DrawExtendedServiceSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RimMindLayoutScope? scope = null)
        {
            GUI.color = new Color(0.5f, 0.9f, 0.6f);
            listing.Label("RimMind.Settings.ModelService.ActiveTitle".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ModelService.ActiveDesc".Translate());
            GUI.color = Color.white;
            listing.LabelWithTooltip(
                "RimMind.Settings.ModelService.ActiveTitle".Translate(),
                "RimMind.Settings.ModelService.ActiveDesc".Translate(),
                new Color(0.5f, 0.9f, 0.6f));
            listing.Gap(4f);

            bool isInstalled = IsModelServiceInstalled();
            if (isInstalled)
            {
                var (nodeCount, activeCount, strategyDesc, primaryDesc) = GetModelServiceStatusSummary();

                Rect infoCard = listing.GetRect(48f);
                Widgets.DrawBoxSolid(infoCard, new Color(0.12f, 0.16f, 0.22f, 0.7f));
                Widgets.DrawHighlightIfMouseover(infoCard);
                TooltipHandler.TipRegion(infoCard, "RimMind.Settings.ModelService.ActiveDesc".Translate());

                Rect text1 = new Rect(infoCard.x + 8f, infoCard.y + 4f, infoCard.width - 16f, 20f);
                Widgets.Label(text1, $"{"RimMind.Settings.ModelService.ConfiguredNodes".Translate()}: {nodeCount} ({"RimMind.Settings.ModelService.ActiveNodes".Translate()}: {activeCount}) | {"RimMind.Settings.ModelService.Strategy".Translate()}: {strategyDesc}");

                Rect text2 = new Rect(infoCard.x + 8f, infoCard.y + 24f, infoCard.width - 16f, 20f);
                GUI.color = Color.gray;
                Widgets.Label(text2, $"{"RimMind.Settings.ModelService.PrimaryEndpoint".Translate()}: {primaryDesc}");
                GUI.color = Color.white;

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
                GUI.color = new Color(1f, 0.7f, 0.3f);
                listing.Label("RimMind.Settings.ModelServiceNotInstalledNote".Translate());
                GUI.color = Color.white;
            }
        }

        private static (int nodeCount, int activeCount, string strategy, string primaryDesc) GetModelServiceStatusSummary()
        {
            try
            {
                var modType = GenTypes.GetTypeInAnyAssembly("RimMind.ModelService.RimMindModelServiceMod");
                if (modType == null) return (0, 0, "N/A", "N/A");

                var settingsProp = modType.GetProperty("Settings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var settings = settingsProp?.GetValue(null);
                if (settings == null) return (0, 0, "N/A", "N/A");

                var endpointsProp = settings.GetType().GetProperty("endpoints") ?? settings.GetType().GetField("endpoints") as System.Reflection.MemberInfo;
                System.Collections.IEnumerable? endpoints = null;
                if (endpointsProp is System.Reflection.PropertyInfo epi)
                    endpoints = epi.GetValue(settings) as System.Collections.IEnumerable;
                else if (endpointsProp is System.Reflection.FieldInfo efi)
                    endpoints = efi.GetValue(settings) as System.Collections.IEnumerable;

                int count = 0;
                int active = 0;
                string primary = "None";

                if (endpoints != null)
                {
                    foreach (var ep in endpoints)
                    {
                        count++;
                        var enabledProp = ep.GetType().GetProperty("isEnabled") ?? ep.GetType().GetField("isEnabled") as System.Reflection.MemberInfo;
                        bool isEnabled = false;
                        if (enabledProp is System.Reflection.PropertyInfo pi)
                            isEnabled = (bool)(pi.GetValue(ep) ?? false);
                        else if (enabledProp is System.Reflection.FieldInfo fi)
                            isEnabled = (bool)(fi.GetValue(ep) ?? false);

                        if (isEnabled)
                        {
                            active++;
                            if (primary == "None")
                            {
                                var nameField = ep.GetType().GetField("name");
                                var urlField = ep.GetType().GetField("endpoint");
                                primary = $"{nameField?.GetValue(ep)} ({urlField?.GetValue(ep)})";
                            }
                        }
                    }
                }

                var stratProp = settings.GetType().GetField("balancingStrategy") ?? settings.GetType().GetProperty("balancingStrategy") as System.Reflection.MemberInfo;
                string strategy = "PriorityFailover";
                if (stratProp is System.Reflection.FieldInfo sfi)
                    strategy = sfi.GetValue(settings)?.ToString() ?? strategy;
                else if (stratProp is System.Reflection.PropertyInfo spi)
                    strategy = spi.GetValue(settings)?.ToString() ?? strategy;

                return (count, active, strategy, primary);
            }
            catch
            {
                return (0, 0, "Unknown", "Unknown");
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
            listing.LabelWithTooltip($"{"RimMind.Settings.MaxTokens".Translate()}: {s.MaxTokens}", "RimMind.Settings.MaxTokens.Desc".Translate());
            s.MaxTokens = (int)listing.SliderWithTooltip(s.MaxTokens, 200f, 2000f, "RimMind.Settings.MaxTokens.Desc".Translate());

            listing.Label($"{"RimMind.Settings.Temperature".Translate()}: {s.DefaultTemperature:F2}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Temperature.Desc".Translate());
            GUI.color = Color.white;
            s.DefaultTemperature = listing.Slider(s.DefaultTemperature, 0f, 2f);
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
            listing.Label("RimMind.UI.FlywheelAutoApply".Translate());
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

            listing.Label("RimMind.UI.FlywheelConfidence".Translate(s.AutoApplyConfidenceThreshold));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.UI.FlywheelConfidence.Desc".Translate());
            GUI.color = Color.white;
            s.AutoApplyConfidenceThreshold = listing.Slider(s.AutoApplyConfidenceThreshold, 0.5f, 1.0f);
            listing.LabelWithTooltip("RimMind.UI.FlywheelConfidence".Translate(s.AutoApplyConfidenceThreshold), "RimMind.UI.FlywheelConfidence.Desc".Translate());
            s.AutoApplyConfidenceThreshold = listing.SliderWithTooltip(s.AutoApplyConfidenceThreshold, 0.5f, 1.0f, "RimMind.UI.FlywheelConfidence.Desc".Translate());
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
            listing.LabelWithTooltip($"{"RimMind.Settings.MaxConcurrent".Translate()}: {s.MaxConcurrentRequests}", "RimMind.Settings.MaxConcurrent.Desc".Translate());
            s.MaxConcurrentRequests = (int)listing.SliderWithTooltip(s.MaxConcurrentRequests, 1f, 10f, "RimMind.Settings.MaxConcurrent.Desc".Translate());

            listing.Label($"{"RimMind.Settings.MaxRetry".Translate()}: {s.MaxRetryCount}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.MaxRetry.Desc".Translate());
            GUI.color = Color.white;
            s.MaxRetryCount = (int)listing.Slider(s.MaxRetryCount, 0f, 5f);
            listing.LabelWithTooltip($"{"RimMind.Settings.MaxRetry".Translate()}: {s.MaxRetryCount}", "RimMind.Settings.MaxRetry.Desc".Translate());
            s.MaxRetryCount = (int)listing.SliderWithTooltip(s.MaxRetryCount, 0f, 5f, "RimMind.Settings.MaxRetry.Desc".Translate());

            listing.Label($"{"RimMind.Settings.RequestTimeout".Translate()}: {s.RequestTimeoutMs / 1000}s");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.RequestTimeout.Desc".Translate());
            GUI.color = Color.white;
            s.RequestTimeoutMs = (int)listing.Slider(s.RequestTimeoutMs / 1000f, 10f, 300f) * 1000;
            listing.LabelWithTooltip($"{"RimMind.Settings.RequestTimeout".Translate()}: {s.RequestTimeoutMs / 1000}s", "RimMind.Settings.RequestTimeout.Desc".Translate());
            s.RequestTimeoutMs = (int)listing.SliderWithTooltip(s.RequestTimeoutMs / 1000f, 10f, 300f, "RimMind.Settings.RequestTimeout.Desc".Translate()) * 1000;

            listing.Label($"{"RimMind.Settings.RequestExpireTicks".Translate()}: {s.RequestExpireTicks / 60f:F0}s ({s.RequestExpireTicks} ticks)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.RequestExpireTicks.Desc".Translate());
            GUI.color = Color.white;
            s.RequestExpireTicks = (int)listing.Slider(s.RequestExpireTicks, 6000f, 120000f);
            listing.LabelWithTooltip($"{"RimMind.Settings.RequestExpireTicks".Translate()}: {s.RequestExpireTicks / 60f:F0}s ({s.RequestExpireTicks} ticks)", "RimMind.Settings.RequestExpireTicks.Desc".Translate());
            s.RequestExpireTicks = (int)listing.SliderWithTooltip(s.RequestExpireTicks, 6000f, 120000f, "RimMind.Settings.RequestExpireTicks.Desc".Translate());

            listing.Label($"{"RimMind.Settings.BehaviorHistoryMax".Translate()}: {s.BehaviorHistoryMax}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());
            GUI.color = Color.white;
            s.BehaviorHistoryMax = (int)listing.Slider(s.BehaviorHistoryMax, 10f, 500f);
            listing.LabelWithTooltip($"{"RimMind.Settings.BehaviorHistoryMax".Translate()}: {s.BehaviorHistoryMax}", "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());
            s.BehaviorHistoryMax = (int)listing.SliderWithTooltip(s.BehaviorHistoryMax, 10f, 500f, "RimMind.Settings.BehaviorHistoryMax.Desc".Translate());

            listing.Label($"{"RimMind.Settings.QueueProcessInterval".Translate()}: {s.QueueProcessInterval} ticks ({s.QueueProcessInterval / 60f:F1}s)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.QueueProcessInterval.Desc".Translate());
            GUI.color = Color.white;
            s.QueueProcessInterval = (int)listing.Slider(s.QueueProcessInterval, 10f, 300f);
            listing.LabelWithTooltip($"{"RimMind.Settings.QueueProcessInterval".Translate()}: {s.QueueProcessInterval} ticks ({s.QueueProcessInterval / 60f:F1}s)", "RimMind.Settings.QueueProcessInterval.Desc".Translate());
            s.QueueProcessInterval = (int)listing.SliderWithTooltip(s.QueueProcessInterval, 10f, 300f, "RimMind.Settings.QueueProcessInterval.Desc".Translate());

            listing.Label($"{"RimMind.Settings.DefaultModCooldown".Translate()}: {s.DefaultModCooldownTicks / 60f:F0}s ({s.DefaultModCooldownTicks} ticks)");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.DefaultModCooldown.Desc".Translate());
            GUI.color = Color.white;
            s.DefaultModCooldownTicks = (int)listing.Slider(s.DefaultModCooldownTicks, 600f, 36000f);
            listing.LabelWithTooltip($"{"RimMind.Settings.DefaultModCooldown".Translate()}: {s.DefaultModCooldownTicks / 60f:F0}s ({s.DefaultModCooldownTicks} ticks)", "RimMind.Settings.DefaultModCooldown.Desc".Translate());
            s.DefaultModCooldownTicks = (int)listing.SliderWithTooltip(s.DefaultModCooldownTicks, 600f, 36000f, "RimMind.Settings.DefaultModCooldown.Desc".Translate());

            var queue = RequestQueue.ResolveOptional(runtimeScope);
            if (queue != null)
            {
                listing.Gap(4f);
                GUI.color = Color.gray;
                listing.Label("RimMind.Settings.QueueSeeTab".Translate());
                GUI.color = Color.white;
                listing.LabelWithTooltip("RimMind.Settings.QueueSeeTab".Translate(), "RimMind.Settings.QueueSeeTab".Translate(), Color.gray);
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
            listing.LabelWithTooltip("RimMind.Settings.ApiKey".Translate(), "RimMind.Settings.ApiKey.Desc".Translate());
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
                TooltipHandler.TipRegion(field, "RimMind.Settings.ApiKey.Desc".Translate());
                if (Widgets.ButtonText(toggle, _showApiKey ? "RimMind.Settings.Hide".Translate() : "RimMind.Settings.Show".Translate()))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            listing.Label("RimMind.Settings.ApiEndpoint".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ApiEndpoint.Desc".Translate());
            GUI.color = Color.white;
            s.ApiEndpoint = listing.TextEntry(s.ApiEndpoint);
            listing.LabelWithTooltip("RimMind.Settings.ApiEndpoint".Translate(), "RimMind.Settings.ApiEndpoint.Desc".Translate());
            s.ApiEndpoint = listing.TextEntryWithTooltip(s.ApiEndpoint, "RimMind.Settings.ApiEndpoint.Desc".Translate());

            listing.Gap(4f);
            listing.Label("RimMind.Settings.ModelName".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.ModelName.Desc".Translate());
            GUI.color = Color.white;
            s.ModelName = listing.TextEntry(s.ModelName);
            listing.LabelWithTooltip("RimMind.Settings.ModelName".Translate(), "RimMind.Settings.ModelName.Desc".Translate());
            s.ModelName = listing.TextEntryWithTooltip(s.ModelName, "RimMind.Settings.ModelName.Desc".Translate());
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
            listing.LabelWithTooltip("RimMind.Settings.Provider.Player2".Translate(), "RimMind.Settings.Player2.Desc".Translate());
            listing.Gap(4f);

            listing.Label("RimMind.Settings.ApiKey".Translate() + " (" + "RimMind.Settings.Player2.ApiKeyOptional".Translate() + ")");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Player2.ApiKeyDesc".Translate());
            GUI.color = Color.white;
            listing.LabelWithTooltip("RimMind.Settings.ApiKey".Translate() + " (" + "RimMind.Settings.Player2.ApiKeyOptional".Translate() + ")", "RimMind.Settings.Player2.ApiKeyDesc".Translate());
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
            listing.Label("RimMind.Settings.Player2.RemoteUrl".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Settings.Player2.RemoteUrl.Desc".Translate());
            GUI.color = Color.white;
            s.Player2RemoteUrl = listing.TextEntry(s.Player2RemoteUrl);
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
