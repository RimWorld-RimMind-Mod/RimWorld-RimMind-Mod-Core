using System;
using System.Threading;

namespace RimMind.Domain.ValueObjects
{
    public static class TraceContext
    {
        private static readonly AsyncLocal<string?> _current = new();
        public static string? Current => _current.Value;

        public static IDisposable BeginScope(string traceId)
        {
            var prev = _current.Value;
            _current.Value = traceId;
            return new Scope(prev);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string? _prev;
            public Scope(string? prev) => _prev = prev;
            public void Dispose() => _current.Value = _prev;
        }
    }
}
