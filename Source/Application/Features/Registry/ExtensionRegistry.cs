using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Features.Registry
{
    public sealed class ExtensionRegistry<T> : IExtensionRegistry<T>
        where T : class, IExtension
    {
        private readonly ConcurrentDictionary<string, T> _extensions
            = new ConcurrentDictionary<string, T>();
        private readonly ILogSink? _log;

        public ExtensionRegistry(ILogSink? log = null) { _log = log; }

        public void Register(T extension)
        {
            if (extension == null) return;
            // Duplicate ID warning: if TryAdd fails, a registration with the same ID already exists.
            // We overwrite with the new extension, but log a warning for diagnostics.
            if (!_extensions.TryAdd(extension.Id, extension))
            {
                _log?.Message($"[ExtensionRegistry] Duplicate ID '{extension.Id}' for {typeof(T).Name}, overwriting previous registration");
                _extensions[extension.Id] = extension;
            }
            else
            {
                _log?.Message($"[ExtensionRegistry] Registered {typeof(T).Name}: {extension.Id}");
            }
        }

        public bool Unregister(string id)
        {
            return _extensions.TryRemove(id, out _);
        }

        public int UnregisterByOwner(string ownerModId)
        {
            var toRemove = _extensions.Values.Where(e => e.OwnerModId == ownerModId).ToList();
            foreach (var ext in toRemove)
            {
                _extensions.TryRemove(ext.Id, out _);
            }
            return toRemove.Count;
        }

        public IReadOnlyList<T> All
        {
            get { return new List<T>(_extensions.Values); }
        }

        public T? FindById(string id)
        {
            return _extensions.TryGetValue(id, out var ext) ? ext : null;
        }
    }
}
