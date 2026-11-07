using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.UI.DebugCenter.Pages;
using RimMind.Infrastructure.UI.DebugTables;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public static class DebugCenterPageRegistry
    {
        private static readonly object SyncRoot = new();
        private static readonly List<DebugCenterPageRegistration> Pages = new();

        static DebugCenterPageRegistry()
        {
            ResetToDefaults();
        }

        public static string DefaultPageId
            => GetAll().FirstOrDefault(page => page.IsDefault)?.Id
                ?? GetAll().FirstOrDefault()?.Id
                ?? string.Empty;

        public static void ResetToDefaults()
        {
            lock (SyncRoot)
            {
                Pages.Clear();
                RegisterBuiltInPages();
            }
        }

        private static void RegisterBuiltInPages()
        {
            Register(new DebugCenterPageDescriptor(
                "overview",
                "RimMind.UI.Hub.Tab.Overview",
                0,
                IsDefault: true,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new OverviewDebugCenterPageDrawer());

            Register(new DebugCenterPageDescriptor(
                "agents",
                "RimMind.UI.Hub.Tab.Agents",
                10,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new AgentsDebugCenterPageDrawer());

            Register(new DebugCenterPageDescriptor(
                "ai_requests",
                "RimMind.UI.Hub.Tab.AIRequests",
                20,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new AIRequestsDebugCenterPageDrawer(log: null));

            Register(new DebugCenterPageDescriptor(
                "tool_calls",
                "RimMind.UI.Hub.Tab.ToolCalls",
                30,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new ToolCallsDebugCenterPageDrawer(new ToolCallsDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "mechanisms",
                "RimMind.UI.Hub.Tab.Mechanisms",
                40,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new MechanismsDebugCenterPageDrawer(new MechanismsDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "context_keys",
                "RimMind.UI.Hub.Tab.ContextKeys",
                50,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new ContextKeysDebugCenterPageDrawer(new ContextKeysDebugTableModelBuilder()));

            Register(new DebugCenterPageDescriptor(
                "settings",
                "RimMind.UI.Hub.Tab.Settings",
                60,
                IsDefault: false,
                OwnerModId: RimMindOwnerConsts.CoreModId), () => new SettingsEntryDebugCenterPageDrawer());
        }

        public static void Register(DebugCenterPageDescriptor descriptor, Func<IDebugCenterPageDrawer> factory)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException("Page descriptor Id cannot be null or whitespace.", nameof(descriptor));

            var normalized = string.IsNullOrWhiteSpace(descriptor.OwnerModId)
                ? descriptor with { OwnerModId = RimMindOwnerConsts.CoreModId }
                : descriptor;

            lock (SyncRoot)
            {
                Pages.RemoveAll(existing => existing.Descriptor.Id == normalized.Id);
                Pages.Add(new DebugCenterPageRegistration(normalized, factory));
            }
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (SyncRoot)
            {
                return Pages.RemoveAll(existing => existing.Descriptor.Id == id) > 0;
            }
        }

        public static int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null)
                throw new ArgumentNullException(nameof(ownerModId));

            lock (SyncRoot)
            {
                return Pages.RemoveAll(existing =>
                    string.Equals(existing.Descriptor.OwnerModId, ownerModId, StringComparison.Ordinal));
            }
        }

        public static DebugCenterPageDescriptor? Find(string id)
            => GetAll().FirstOrDefault(page => page.Id == id);

        public static IReadOnlyList<DebugCenterPageDescriptor> GetAll()
        {
            lock (SyncRoot)
            {
                return Pages
                    .OrderBy(page => page.Descriptor.Order)
                    .ThenBy(page => page.Descriptor.Id, StringComparer.Ordinal)
                    .Select(page => page.Descriptor)
                    .ToList();
            }
        }

        public static IDebugCenterPageDrawer? Create(string id)
            => CreateAllRegistrations()
                .FirstOrDefault(page => page.Descriptor.Id == id)
                ?.CreateDrawer();

        public static IReadOnlyList<IDebugCenterPageDrawer> CreateAll()
            => CreateAllRegistrations()
                .Select(page => page.CreateDrawer())
                .ToList();

        public static IReadOnlyList<DebugCenterPageRegistration> CreateAllRegistrations()
        {
            lock (SyncRoot)
            {
                return Pages
                    .OrderBy(page => page.Descriptor.Order)
                    .ThenBy(page => page.Descriptor.Id, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }
}
