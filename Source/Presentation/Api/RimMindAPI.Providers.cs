using System;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Verse;
using System.Collections.Generic;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Providers
        {
            private static readonly RuntimeServiceRef<IProviderRegistry> Registries =
                RuntimeServiceRef<IProviderRegistry>.Required();

            /// <summary>
            /// Registers a Pawn string-provider candidate. Callers must provide a stable owner ID; candidates are
            /// selected by priority, and <paramref name="overrideExisting"/> replaces only the same owner's candidate.
            /// </summary>
            public static void RegisterPawnProvider(
                string category,
                string ownerModId,
                Func<Pawn, string?> provider,
                int priority = 0,
                bool overrideExisting = false)
            {
                if (provider == null)
                    throw new ArgumentNullException(nameof(provider));

                Registries.Value.RegisterPawnProvider(
                    category,
                    ownerModId,
                    value => value is Pawn pawn ? provider(pawn) : null,
                    priority,
                    overrideExisting);
            }

            /// <summary>
            /// Registers a static string-provider candidate. Callers must provide a stable owner ID; candidates are
            /// selected by priority.
            /// </summary>
            public static void RegisterStaticProvider(
                string category,
                string ownerModId,
                Func<string?> provider,
                int priority = 0)
            {
                if (provider == null)
                    throw new ArgumentNullException(nameof(provider));

                Registries.Value.RegisterStaticProvider(
                    category,
                    ownerModId,
                    provider,
                    priority);
            }

            public static Result<string?, RimMindError> GetProviderData(string category, Pawn pawn)
                => Registries.Value.GetProviderData(category, pawn);

            public static Result<string?, RimMindError> GetStaticProviderData(string category)
                => Registries.Value.GetStaticProviderData(category);

            public static List<string> GetRegisteredCategories()
                => Registries.Value.GetRegisteredCategories();

            public static int UnregisterByOwner(string ownerModId)
                => Registries.Value.UnregisterByOwner(ownerModId);
        }
    }
}
