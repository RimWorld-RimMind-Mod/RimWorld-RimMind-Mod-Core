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

        private sealed class BuiltinSettingsTab
        {
            public BuiltinSettingsTab(
                string id,
                string labelKey,
                string fallbackLabel,
                System.Action<Rect, ISettingsProvider, RuntimeServiceScope, RimMindLayoutScope?> draw)
            {
                Id = id;
                LabelKey = labelKey;
                FallbackLabel = fallbackLabel;
                Draw = draw;
            }

            public string Id { get; }
            public string LabelKey { get; }
            public string FallbackLabel { get; }
            public System.Action<Rect, ISettingsProvider, RuntimeServiceScope, RimMindLayoutScope?> Draw { get; }
        }

        private static readonly BuiltinSettingsTab[] BuiltinTabs =
        {
            new("api", "RimMind.Settings.Tab.Api", "API",
                (content, settings, runtimeScope, scope) => ApiTabDrawer.Draw(content, settings, runtimeScope, scope)),
            new("queue", "RimMind.Settings.Tab.Queue", "Queue",
                (content, settings, runtimeScope, scope) => QueueTabDrawer.Draw(content, settings, runtimeScope, scope)),
            new("prompts", "RimMind.Settings.Tab.Prompts", "Prompts",
                (content, settings, _, scope) => PromptsTabDrawer.Draw(content, settings, scope)),
            new("context", "RimMind.Settings.Tab.Context", "Context",
                (content, settings, _, scope) => ContextTabDrawer.Draw(content, settings, scope)),
        };

        private static void DrawCurrentSettingsPage(
            Rect content,
            ISettingsProvider settings,
            IExtensionRegistry<ISettingsTab>? settingsTabRegistry,
            RuntimeServiceScope runtimeScope,
            RimMindLayoutScope? scope)
        {
            for (int i = 0; i < BuiltinTabs.Length; i++)
            {
                if (BuiltinTabs[i].Id == _curTab)
                {
                    BuiltinTabs[i].Draw(content, settings, runtimeScope, scope);
                    return;
                }
            }

            if (settingsTabRegistry != null)
            {
                foreach (var tab in settingsTabRegistry.All)
                {
                    if (tab.Id == _curTab)
                    {
                        if (tab is IRuntimeScopedSettingsTab runtimeScopedTab)
                            runtimeScopedTab.Draw(content, runtimeScope);
                        else
                            tab.Draw(content);
                        break;
                    }
                }
            }
        }

        private static List<TabbedPageTabModel> CollectTabs(
            IExtensionRegistry<ISettingsTab>? settingsTabRegistry)
        {
            var tabs = new List<TabbedPageTabModel>(BuiltinTabs.Length + (settingsTabRegistry?.All.Count ?? 0));
            for (int i = 0; i < BuiltinTabs.Length; i++)
            {
                var builtin = BuiltinTabs[i];
                tabs.Add(CreateTab(builtin.Id, builtin.LabelKey, builtin.FallbackLabel));
            }

            if (settingsTabRegistry != null)
                foreach (var tab in settingsTabRegistry.All)
                    tabs.Add(new TabbedPageTabModel(tab.Id, tab.Label, tab.Id, _curTab == tab.Id, true, null));
            return tabs;
        }

        private static void EnsureCurrentTab(IExtensionRegistry<ISettingsTab>? settingsTabRegistry)
        {
            for (int i = 0; i < BuiltinTabs.Length; i++)
            {
                if (BuiltinTabs[i].Id == _curTab)
                    return;
            }

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

        private static TabbedPageTabModel CreateTab(string id, string labelKey, string fallbackLabel)
            => new(id, UiText.Get(labelKey, fallbackLabel), labelKey, _curTab == id, true, null);

    }

    internal interface IRuntimeScopedSettingsTab
    {
        void Draw(Rect rect, RuntimeServiceScope runtimeScope);
    }
}
