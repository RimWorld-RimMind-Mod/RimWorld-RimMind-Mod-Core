using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IContextKeyRegistry
    {
        void Register(KeyMeta meta);
        void Register(ContextProviderDef def);
        bool Unregister(string key);
        IReadOnlyList<KeyMeta> GetAll();
        KeyMeta? Get(string key);
        void Clear();
    }
}
