using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Runtime
{
    public class ProviderRegistry : IProviderRegistry
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, OwnedProvider<Func<object, string?>>>> _pawnProviders = new ConcurrentDictionary<string, ConcurrentDictionary<string, OwnedProvider<Func<object, string?>>>>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, OwnedProvider<Func<string?>>>> _staticProviders = new ConcurrentDictionary<string, ConcurrentDictionary<string, OwnedProvider<Func<string?>>>>();
        public void RegisterPawnProvider(string category, string modId, Func<object, string?> provider, int priority, bool overrideExisting)
        {
            ValidateOwnerModId(modId, nameof(modId));
            if (string.IsNullOrEmpty(category) || provider == null) return;
            var registrations = _pawnProviders.GetOrAdd(
                category,
                _ => new ConcurrentDictionary<string, OwnedProvider<Func<object, string?>>>(StringComparer.Ordinal));
            var registration = new OwnedProvider<Func<object, string?>>(modId, priority, provider);
            if (overrideExisting)
                registrations.AddOrUpdate(modId, registration, (_, __) => registration);
            else
                registrations.TryAdd(modId, registration);
        }

        public void RegisterStaticProvider(string category, string modId, Func<string?> provider, int priority)
        {
            ValidateOwnerModId(modId, nameof(modId));
            if (string.IsNullOrEmpty(category) || provider == null) return;
            var registrations = _staticProviders.GetOrAdd(
                category,
                _ => new ConcurrentDictionary<string, OwnedProvider<Func<string?>>>(StringComparer.Ordinal));
            var registration = new OwnedProvider<Func<string?>>(modId, priority, provider);
            registrations.AddOrUpdate(modId, registration, (_, __) => registration);
        }

        public Result<string?, RimMindError> GetProviderData(string category, object pawn)
        {
            if (string.IsNullOrEmpty(category))
                return Result<string?, RimMindError>.Err(RimMindErrors.Internal("Category is empty"));

            if (_pawnProviders.TryGetValue(category, out var registrations)
                && TrySelectProvider(registrations, out var provider))
                return ExecuteProvider(() => provider(pawn));

            return Result<string?, RimMindError>.Err(RimMindErrors.Internal($"No provider registered for category: {category}"));
        }

        public Result<string?, RimMindError> GetStaticProviderData(string category)
        {
            if (string.IsNullOrEmpty(category))
                return Result<string?, RimMindError>.Err(RimMindErrors.Internal("Category is empty"));

            if (_staticProviders.TryGetValue(category, out var registrations)
                && TrySelectProvider(registrations, out var provider))
                return ExecuteProvider(provider);

            return Result<string?, RimMindError>.Err(RimMindErrors.Internal($"No static provider registered for category: {category}"));
        }

        private static Result<string?, RimMindError> ExecuteProvider(Func<string?> provider)
        {
            try
            {
                return Result<string?, RimMindError>.Ok(provider());
            }
            catch (Exception ex)
            {
                return Result<string?, RimMindError>.Err(RimMindErrors.Internal(ex.Message, ex));
            }
        }

        public List<string> GetRegisteredCategories()
        {
            var categorySet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _pawnProviders)
            {
                if (!entry.Value.IsEmpty)
                    categorySet.Add(entry.Key);
            }
            foreach (var entry in _staticProviders)
            {
                if (!entry.Value.IsEmpty)
                    categorySet.Add(entry.Key);
            }

            var categories = new List<string>(categorySet);
            categories.Sort(StringComparer.Ordinal);
            return categories;
        }

        public int UnregisterByOwner(string ownerModId)
        {
            ValidateOwnerModId(ownerModId, nameof(ownerModId));

            var removed = 0;
            foreach (var registrations in _pawnProviders.Values)
            {
                if (registrations.TryRemove(ownerModId, out _))
                    removed++;
            }
            foreach (var registrations in _staticProviders.Values)
            {
                if (registrations.TryRemove(ownerModId, out _))
                    removed++;
            }
            return removed;
        }

        private static void ValidateOwnerModId(string ownerModId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(ownerModId))
                throw new ArgumentException("Owner mod ID cannot be empty or whitespace.", parameterName);
        }

        public void Reset()
        {
            _pawnProviders.Clear();
            _staticProviders.Clear();
        }

        private static bool TrySelectProvider<TProvider>(
            ConcurrentDictionary<string, OwnedProvider<TProvider>> registrations,
            out TProvider provider)
            where TProvider : class
        {
            OwnedProvider<TProvider>? selected = null;
            foreach (var candidate in registrations.Values)
            {
                if (selected == null
                    || candidate.Priority > selected.Priority
                    || (candidate.Priority == selected.Priority
                        && string.CompareOrdinal(candidate.OwnerModId, selected.OwnerModId) < 0))
                {
                    selected = candidate;
                }
            }

            provider = selected?.Provider!;
            return selected != null;
        }

        private sealed class OwnedProvider<TProvider>
            where TProvider : class
        {
            public OwnedProvider(string ownerModId, int priority, TProvider provider)
            {
                OwnerModId = ownerModId;
                Priority = priority;
                Provider = provider;
            }

            public string OwnerModId { get; }
            public int Priority { get; }
            public TProvider Provider { get; }
        }
    }
}
