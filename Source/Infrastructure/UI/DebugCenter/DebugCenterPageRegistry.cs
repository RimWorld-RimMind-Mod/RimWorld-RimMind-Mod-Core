using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.UI.DebugCenter.Pages;
using RimMind.Infrastructure.UI.DebugTables;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public static class DebugCenterPageRegistry
    {
        private static readonly List<DebugCenterPageRegistration> Pages = new();

        static DebugCenterPageRegistry()
        {
            Register(new DebugCenterPageDescriptor(
                "overview",
                "RimMind.UI.Hub.Tab.Overview",
                0,
                IsDefault: true), () => new OverviewDebugCenterPageDrawer());

            Register(new DebugCenterPageDescriptor(
                "agents",
                "RimMind.UI.Hub.Tab.Agents",
                10,
                IsDefault: false), () => new AgentsDebugCenterPageDrawer());

            Register(new DebugCenterPageDescriptor(
                "ai_requests",
                "RimMind.UI.Hub.Tab.AIRequests",
                20,
                IsDefault: false), () => new AIRequestsDebugCenterPageDrawer(log: null));

            Register(new DebugCenterPageDescriptor(
                "tool_calls",
                "RimMind.UI.Hub.Tab.ToolCalls",
                30,
                IsDefault: false), () => new ToolCallsDebugCenterPageDrawer(new ToolCallsDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "mechanisms",
                "RimMind.UI.Hub.Tab.Mechanisms",
                40,
                IsDefault: false), () => new MechanismsDebugCenterPageDrawer(new MechanismsDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "context_keys",
                "RimMind.UI.Hub.Tab.ContextKeys",
                50,
                IsDefault: false), () => new ContextKeysDebugCenterPageDrawer(new ContextKeysDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "settings",
                "RimMind.UI.Hub.Tab.Settings",
                60,
                IsDefault: false), () => new SettingsEntryDebugCenterPageDrawer());
        }

        public static string DefaultPageId
            => GetAll().FirstOrDefault(page => page.IsDefault)?.Id
                ?? GetAll().FirstOrDefault()?.Id
                ?? string.Empty;

        public static void Register(DebugCenterPageDescriptor descriptor, Func<IDebugCenterPageDrawer> factory)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            Pages.RemoveAll(existing => existing.Descriptor.Id == descriptor.Id);
            Pages.Add(new DebugCenterPageRegistration(descriptor, factory));
        }

        public static DebugCenterPageDescriptor? Find(string id)
            => GetAll().FirstOrDefault(page => page.Id == id);

        public static IReadOnlyList<DebugCenterPageDescriptor> GetAll()
            => Pages
                .OrderBy(page => page.Descriptor.Order)
                .ThenBy(page => page.Descriptor.Id, StringComparer.Ordinal)
                .Select(page => page.Descriptor)
                .ToList();

        public static IDebugCenterPageDrawer? Create(string id)
            => CreateAllRegistrations()
                .FirstOrDefault(page => page.Descriptor.Id == id)
                ?.CreateDrawer();

        public static IReadOnlyList<IDebugCenterPageDrawer> CreateAll()
            => CreateAllRegistrations()
                .Select(page => page.CreateDrawer())
                .ToList();

        public static IReadOnlyList<DebugCenterPageRegistration> CreateAllRegistrations()
            => Pages
                .OrderBy(page => page.Descriptor.Order)
                .ThenBy(page => page.Descriptor.Id, StringComparer.Ordinal)
                .ToList();
    }
}
