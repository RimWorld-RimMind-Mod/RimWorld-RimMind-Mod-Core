using System;
using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IProviderRegistry
    {
        void RegisterStaticProvider(string category, string modId, Func<string?> provider, int priority);
        /// <summary>
        /// Registers one pawn provider per owner/category pair. When <paramref name="overrideExisting"/> is false,
        /// an existing registration from that owner is preserved; true replaces only that owner's candidate.
        /// Candidates from other owners remain available as priority-ordered fallbacks.
        /// </summary>
        void RegisterPawnProvider(string category, string modId, Func<object, string?> provider, int priority, bool overrideExisting);
        Result<string?, RimMindError> GetProviderData(string category, object pawn);
        Result<string?, RimMindError> GetStaticProviderData(string category);
        List<string> GetRegisteredCategories();
        /// <summary>Removes every pawn and static provider owned by <paramref name="ownerModId"/>.</summary>
        int UnregisterByOwner(string ownerModId);
        void Reset();
    }
}
