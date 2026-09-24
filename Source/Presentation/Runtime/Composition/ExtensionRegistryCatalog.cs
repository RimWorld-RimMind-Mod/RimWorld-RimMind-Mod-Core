using System;
using System.Collections.Concurrent;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Features.Registry;

namespace RimMind.Presentation.Runtime.Composition
{
    internal sealed class ExtensionRegistryCatalog
    {
        private readonly ConcurrentDictionary<Type, IRegistryEntry> _registries =
            new ConcurrentDictionary<Type, IRegistryEntry>();

        public IExtensionRegistry<T> GetExtensionRegistry<T>()
            where T : class, IExtension
        {
            return ((RegistryEntry<T>)_registries.GetOrAdd(
                typeof(T),
                _ => new RegistryEntry<T>())).Registry;
        }

        public ExtensionRegistryCatalog Fork()
        {
            var fork = new ExtensionRegistryCatalog();
            foreach (var pair in _registries)
            {
                fork._registries[pair.Key] = pair.Value.Fork();
            }

            return fork;
        }

        private interface IRegistryEntry
        {
            IRegistryEntry Fork();
        }

        private sealed class RegistryEntry<T> : IRegistryEntry
            where T : class, IExtension
        {
            public ExtensionRegistry<T> Registry { get; } = new ExtensionRegistry<T>();

            public IRegistryEntry Fork()
            {
                var copy = new RegistryEntry<T>();
                foreach (var extension in Registry.All)
                {
                    copy.Registry.Register(extension);
                }

                return copy;
            }
        }
    }
}
