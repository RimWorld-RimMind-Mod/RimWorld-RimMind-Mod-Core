using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Common;

namespace RimMind.Application.Common.Helpers
{
    public sealed record AIProviderDescriptor(
        string ProviderId,
        bool RequiresApiKey = true,
        string? SettingsTabId = null,
        string? DisplayLabelKey = null,
        string OwnerModId = RimMindOwnerConsts.CoreModId);

    public static class AIProviderRegistry
    {
        private static readonly string FallbackProvider = AIProviders.OpenAI;
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, AIProviderDescriptor> Descriptors =
            new(StringComparer.OrdinalIgnoreCase);

        static AIProviderRegistry()
        {
            ResetToDefaults();
        }

        public static void ResetToDefaults()
        {
            lock (SyncRoot)
            {
                Descriptors.Clear();
                RegisterCoreUnlocked(new AIProviderDescriptor(
                    AIProviders.OpenAI,
                    RequiresApiKey: true,
                    SettingsTabId: null,
                    DisplayLabelKey: "RimMind.Settings.Provider.OpenAI",
                    OwnerModId: RimMindOwnerConsts.CoreModId));
                RegisterCoreUnlocked(new AIProviderDescriptor(
                    AIProviders.Player2,
                    RequiresApiKey: false,
                    SettingsTabId: null,
                    DisplayLabelKey: "RimMind.Settings.Provider.Player2",
                    OwnerModId: RimMindOwnerConsts.CoreModId));
            }
        }

        public static void Register(AIProviderDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.ProviderId))
                throw new ArgumentException("ProviderId cannot be null or whitespace.", nameof(descriptor));

            var normalized = string.IsNullOrWhiteSpace(descriptor.OwnerModId)
                ? descriptor with { OwnerModId = RimMindOwnerConsts.CoreModId }
                : descriptor;

            lock (SyncRoot)
            {
                RegisterCoreUnlocked(normalized);
            }
        }

        public static void Register(
            string providerId,
            bool requiresApiKey = true,
            string? settingsTabId = null,
            string? displayLabelKey = null,
            string ownerModId = RimMindOwnerConsts.CoreModId)
        {
            Register(new AIProviderDescriptor(providerId, requiresApiKey, settingsTabId, displayLabelKey, ownerModId));
        }

        public static bool Unregister(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return false;

            lock (SyncRoot)
            {
                return Descriptors.Remove(providerId);
            }
        }

        public static int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null)
                throw new ArgumentNullException(nameof(ownerModId));

            lock (SyncRoot)
            {
                var keys = Descriptors.Values
                    .Where(d => string.Equals(d.OwnerModId, ownerModId, StringComparison.Ordinal))
                    .Select(d => d.ProviderId)
                    .ToList();
                foreach (var key in keys)
                    Descriptors.Remove(key);
                return keys.Count;
            }
        }

        public static AIProviderDescriptor? FindDescriptor(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return null;

            lock (SyncRoot)
            {
                return Descriptors.TryGetValue(providerId!, out var descriptor) ? descriptor : null;
            }
        }

        public static IReadOnlyList<AIProviderDescriptor> GetAllDescriptors()
        {
            lock (SyncRoot)
            {
                return Descriptors.Values.ToList();
            }
        }

        private static void RegisterCoreUnlocked(AIProviderDescriptor descriptor)
        {
            Descriptors[descriptor.ProviderId] = descriptor;
        }

        public static IReadOnlyList<string> GetAllProviderIds(IExtensionRegistry<IAIClientFactory>? registry = null)
        {
            if (registry == null || registry.All.Count == 0)
                return new List<string> { FallbackProvider };
            return registry.All.Select(f => f.ProviderId).ToList();
        }

        public static string GetDefaultProviderId(IExtensionRegistry<IAIClientFactory>? registry = null)
        {
            var ids = GetAllProviderIds(registry);
            return ids.Count > 0 ? ids[0] : FallbackProvider;
        }

        public static bool IsProviderRegistered(string providerId, IExtensionRegistry<IAIClientFactory>? registry = null)
        {
            if (string.IsNullOrEmpty(providerId)) return false;
            if (registry?.FindById(providerId) != null) return true;
            return FindDescriptor(providerId) != null;
        }

        public static bool RequiresApiKey(string providerId, IExtensionRegistry<IAIClientFactory>? registry = null)
        {
            if (string.IsNullOrEmpty(providerId)) return true;
            if (registry != null)
            {
                var factory = registry.FindById(providerId);
                if (factory != null) return factory.RequiresApiKey;
            }

            var descriptor = FindDescriptor(providerId);
            if (descriptor != null)
                return descriptor.RequiresApiKey;

            return true;
        }

        public static string? GetSettingsTabId(string? providerId, IExtensionRegistry<IAIClientFactory>? registry = null)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return null;

            var descriptor = FindDescriptor(providerId);
            if (!string.IsNullOrWhiteSpace(descriptor?.SettingsTabId))
                return descriptor!.SettingsTabId;

            return null;
        }

        public static string? GetDisplayLabelKey(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return null;

            var descriptor = FindDescriptor(providerId);
            if (!string.IsNullOrWhiteSpace(descriptor?.DisplayLabelKey))
                return descriptor!.DisplayLabelKey;

            return null;
        }
    }
}
