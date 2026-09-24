using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    public static class RimMindCoreSettingsUI
    {
        private static readonly RuntimeServiceRef<ISettingsProvider> SettingsProvider =
            RuntimeServiceRef<ISettingsProvider>.Required();
        private static readonly RuntimeServiceRef<IExtensionRegistry<ISettingsTab>> SettingsTabRegistry =
            RuntimeServiceRef<IExtensionRegistry<ISettingsTab>>.Optional();

        private static string _curTab = "api";
        private static readonly RimMindTabbedPageHostDrawer TabHost = new();

        internal static string CurrentTab
        {
            get => _curTab;
            set => _curTab = value;
        }

        internal static void DrawQueueReference(Rect rect, RimMindLayoutScope scope, bool bottom)
        {
            string savedTab = _curTab;
            Vector2 savedScroll = QueueTabDrawer.ScrollPosition;
            try
            {
                _curTab = "queue";
                QueueTabDrawer.ScrollPosition = new Vector2(0, bottom ? 100000f : 0);
                Draw(rect, scope);
            }
            finally
            {
                _curTab = savedTab;
                QueueTabDrawer.ScrollPosition = savedScroll;
            }
        }

        public static void Draw(Rect inRect, RimMindLayoutScope? scope = null)
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var settings = SettingsProvider.Resolve(runtimeScope);
            var settingsTabRegistry = SettingsTabRegistry.ResolveOptional(runtimeScope);
            EnsureCurrentTab(settingsTabRegistry);
            var tabs = CollectTabs(settingsTabRegistry);
            var layout = TabbedPageLayout.Calculate(inRect, tabs);
            _curTab = TabHost.DrawTabs(layout, tabs, _curTab, scope);
            DrawCurrentSettingsPage(layout.Content, settings, settingsTabRegistry, runtimeScope, scope);
        }

        private static void DrawCurrentSettingsPage(
            Rect content,
            ISettingsProvider settings,
            IExtensionRegistry<ISettingsTab>? settingsTabRegistry,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope)
        {
            switch (_curTab)
            {
                case "api": ApiTabDrawer.Draw(content, settings, runtimeScope, scope); break;
                case "queue": QueueTabDrawer.Draw(content, settings, runtimeScope, scope); break;
                case "context": ContextTabDrawer.Draw(content, settings, scope); break;
                case "prompts": PromptsTabDrawer.Draw(content, settings, scope); break;
                default:
                    if (settingsTabRegistry != null)
                        foreach (var tab in settingsTabRegistry.All)
                            if (tab.Id == _curTab)
                            {
                                if (tab is IRuntimeScopedSettingsTab runtimeScopedTab)
                                    runtimeScopedTab.Draw(content, runtimeScope);
                                else
                                    tab.Draw(content);
                                break;
                            }
                    break;
            }
        }

        private static List<TabbedPageTabModel> CollectTabs(
            IExtensionRegistry<ISettingsTab>? settingsTabRegistry)
        {
            var tabs = new List<TabbedPageTabModel>
            {
                CreateTab("api", "RimMind.Settings.Tab.Api"),
                CreateTab("queue", "RimMind.Settings.Tab.Queue"),
                CreateTab("prompts", "RimMind.Settings.Tab.Prompts"),
                CreateTab("context", "RimMind.Settings.Tab.Context"),
            };
            if (settingsTabRegistry != null)
                foreach (var tab in settingsTabRegistry.All)
                    tabs.Add(new TabbedPageTabModel(tab.Id, tab.Label, tab.Id, _curTab == tab.Id, true, null));
            return tabs;
        }

        private static void EnsureCurrentTab(IExtensionRegistry<ISettingsTab>? settingsTabRegistry)
        {
            if (_curTab == "api" || _curTab == "queue" || _curTab == "prompts" || _curTab == "context")
                return;

            if (settingsTabRegistry != null)
            {
                foreach (var tab in settingsTabRegistry.All)
                {
                    if (tab.Id == _curTab)
                        return;
                }
            }

            _curTab = "api";
        }

        private static TabbedPageTabModel CreateTab(string id, string labelKey)
            => new(id, labelKey.Translate(), labelKey, _curTab == id, true, null);

    }

    internal interface IRuntimeScopedSettingsTab
    {
        void Draw(Rect rect, RuntimeServiceScope runtimeScope);
    }
}
