using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Constants;
using Verse;

namespace RimMind.Presentation.UI
{
    /// <summary>
    /// Immutable descriptor for a Bio-tab top-stack badge rendered inside RimWorld's character card.
    /// </summary>
    public sealed record BioTabBadgeDescriptor(
        string Label,
        Func<string>? TooltipProvider = null,
        Action? OnClick = null,
        float ExtraWidth = 16f);

    /// <summary>
    /// Extension interface for submodules that wish to add a badge button to the RimWorld Bio tab
    /// (CharacterCardUtility.DoTopStack) without adding their own Harmony transpiler.
    /// Implementations MUST keep <see cref="TryGetBadge"/> read-only (no save-state mutations on hover/draw).
    /// </summary>
    public interface IBioTabBadgeProvider
    {
        string Id { get; }
        string OwnerModId { get; }
        int Order { get; }
        BioTabBadgeDescriptor? TryGetBadge(Pawn pawn);
    }

    /// <summary>
    /// Thread-safe registry for <see cref="IBioTabBadgeProvider"/> extensions consumed by Core's single
    /// <c>CharacterCardUtility.DoTopStack</c> Harmony patch.
    /// </summary>
    public static class BioTabBadgeRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<IBioTabBadgeProvider> Providers = new List<IBioTabBadgeProvider>();

        public static void Register(IBioTabBadgeProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.Id))
                throw new ArgumentException("Provider Id cannot be null or whitespace.", nameof(provider));

            lock (SyncRoot)
            {
                int idx = Providers.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) Providers[idx] = provider;
                else Providers.Add(provider);
                Providers.Sort((a, b) =>
                {
                    int cmp = a.Order.CompareTo(b.Order);
                    return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (SyncRoot)
            {
                return Providers.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        public static int UnregisterByOwner(string ownerModId)
        {
            if (string.IsNullOrWhiteSpace(ownerModId)) return 0;
            lock (SyncRoot)
            {
                return Providers.RemoveAll(p =>
                    string.Equals(p.OwnerModId ?? RimMindOwnerConsts.CoreModId, ownerModId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static IReadOnlyList<IBioTabBadgeProvider> GetAll()
        {
            lock (SyncRoot)
            {
                return Providers.Count == 0
                    ? Array.Empty<IBioTabBadgeProvider>()
                    : Providers.ToArray();
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Providers.Clear();
            }
        }
    }
}
