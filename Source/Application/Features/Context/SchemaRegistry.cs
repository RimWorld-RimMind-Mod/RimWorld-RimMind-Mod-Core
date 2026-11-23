using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Application.Features.Context
{
    public sealed class SchemaRegistry
    {
        private readonly ConcurrentDictionary<string, string> _schemas
            = new ConcurrentDictionary<string, string>();

        public SchemaRegistry(ILogSink? log = null) { }

        public void Register(string key, string schema) => _schemas[key] = schema;
        public string? Find(string key) => _schemas.TryGetValue(key, out var s) ? s : null;
        public IReadOnlyDictionary<string, string> All => _schemas;
        public void Reset() => _schemas.Clear();
    }
}
