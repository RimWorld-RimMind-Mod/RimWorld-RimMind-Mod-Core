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

            if (!AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry))
            {
                DrawPlayer2Section(listing, s, player2Lifecycle, scope);
            }

            listing.Gap(10f);

            DrawConnectionTestButton(listing, s, runtimeScope, providerRegistry);

            listing.Gap(6f);

            DrawModelBehaviorSection(listing, s, scope);
            DrawRequestSection(listing, s, runtimeScope, scope);
            DrawDebugSection(listing, s, scope);
            DrawFlywheelSection(listing, s, scope);

            listing.End();
            Widgets.EndScrollView();
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

        private static void DrawModelBehaviorSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.ModelBehavior".Translate());
            var forceJsonMode = s.ForceJsonMode;
            listing.CheckboxLabeled(
                "RimMind.Settings.ForceJsonMode".Translate(),
                ref forceJsonMode,
                "RimMind.Settings.ForceJsonModeDesc".Translate());
            s.ForceJsonMode = forceJsonMode;

            var showProgress = s.ShowAgentProgressFloat;
            listing.CheckboxLabeled(
                "RimMind.Settings.ShowAgentProgressFloat".Translate(),
                ref showProgress,
                "RimMind.Settings.ShowAgentProgressFloat.Desc".Translate());
            s.ShowAgentProgressFloat = showProgress;
        }

        private static void DrawRequestSection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Request".Translate());
            scope?.Record(listing.GetRect(0f), "Section:Request");

            DrawModelOutputSubsection(listing, s, scope);
            listing.Gap(8f);
            DrawNetworkRetrySubsection(listing, s, scope);
            listing.Gap(8f);
            DrawAgentCadenceSubsection(listing, s, runtimeScope, scope);
        }

        private static void DrawModelOutputSubsection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.ModelOutput".Translate());
            scope?.Record(listing.GetRect(0f), "Section:ModelOutput");

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
        }

        private static void DrawNetworkRetrySubsection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.NetworkRetry".Translate());
            scope?.Record(listing.GetRect(0f), "Section:NetworkRetry");

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
        }

        private static void DrawAgentCadenceSubsection(
            Listing_Standard listing,
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.AgentCadence".Translate());
            scope?.Record(listing.GetRect(0f), "Section:AgentCadence");

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
            GUI.color = Color.white;
        }

        private static void DrawDebugSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.Section.Debug".Translate());
            var debugLogging = s.DebugLogging;
            listing.CheckboxLabeled("RimMind.Settings.DebugLogging".Translate(), ref debugLogging,
                "RimMind.Settings.DebugLogging.Desc".Translate());
            s.DebugLogging = debugLogging;
        }

        private static void DrawFlywheelSection(Listing_Standard listing, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.UI.FlywheelAutoApply".Translate());
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

    }
}
