using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Extensions
{
    public static class KeyMetaExtensions
    {
        public static ContextProviderDef? GetProviderDef(this KeyMeta meta)
            => meta.Def as ContextProviderDef;

        public static bool HasAsyncProvider(this KeyMeta meta)
            => meta.Def is ContextProviderDef;
    }
}
