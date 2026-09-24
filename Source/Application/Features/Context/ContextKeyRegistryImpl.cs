using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Registry;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class ContextKeyRegistryImpl : IContextKeyRegistry, IOwnedRegistry
    {
        private readonly ConcurrentDictionary<string, KeyMeta> _keys = new ConcurrentDictionary<string, KeyMeta>();
        private readonly object _mutationSync = new object();
        private readonly ILogSink? _logSink;
        private readonly ProviderCache? _providerCache;

        public ContextKeyRegistryImpl(ILogSink? logSink = null, ProviderCache? providerCache = null)
        {
            _logSink = logSink;
            _providerCache = providerCache;
        }

        public void Register(KeyMeta meta)
        {
            KeyMeta? old;
            lock (_mutationSync)
            {
                _keys.TryGetValue(meta.Key, out old);
                if (old != null)
                    meta.OverrideSource = old.OwnerMod ?? "Unknown";

                if (meta.Def is ContextProviderDef def)
                    _providerCache?.ReplaceInvalidation(def);
                else
                    _providerCache?.UnsubscribeInvalidation(meta.Key);

                _keys[meta.Key] = meta;
            }

            if (old != null)
                _logSink?.Warning($"[RimMind-Core] ContextKey '{meta.Key}' registered by '{old.OwnerMod}' " +
                    $"overwritten by '{meta.OwnerMod}'.");
        }

        public void Register(ContextProviderDef def)
        {
            var meta = new KeyMeta(def.Key, def.Layer, def.Priority, _ => new List<ContextEntry>(), def.OwnerMod ?? "Unknown",
                cacheScope: def.CacheScope)
            {
                Def = def
            };
            Register(meta);
        }

        public bool Unregister(string key)
        {
            lock (_mutationSync)
            {
                if (!_keys.TryRemove(key, out _))
                    return false;

                _providerCache?.UnsubscribeInvalidation(key);
                return true;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// KeyMeta 使用 <c>OwnerMod</c> 字段（非 <c>OwnerModId</c>），语义一致。
        /// </remarks>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            lock (_mutationSync)
            {
                var toRemove = _keys.Values
                    .Where(k => k.OwnerMod == ownerModId)
                    .Select(k => k.Key)
                    .ToList();
                var removed = 0;
                foreach (var key in toRemove)
                {
                    if (!_keys.TryRemove(key, out _))
                        continue;

                    _providerCache?.UnsubscribeInvalidation(key);
                    removed++;
                }
                return removed;
            }
        }

        public IReadOnlyList<KeyMeta> GetAll()
        {
            return new List<KeyMeta>(_keys.Values);
        }

        public KeyMeta? Get(string key)
        {
            return _keys.TryGetValue(key, out var meta) ? meta : null;
        }

        public void Clear()
        {
            lock (_mutationSync)
            {
                _keys.Clear();
                _providerCache?.Clear();
            }
        }
    }
}
