using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Diagnostics;

namespace RimMind.Application.Common.Models.Diagnostics
{
    public sealed class InMemorySpanTracer : ISpanTracer
    {
        private readonly ConcurrentBag<SpanRecord> _spans = new();
        private const int MaxSpans = 1000;

        public IReadOnlyList<SpanRecord> GetSpans() => _spans.ToList();
        public IReadOnlyList<SpanRecord> GetSpans(string traceId) => _spans.Where(s => s.ParentId == traceId || s.SpanId == traceId).ToList();
        public void Clear() { while (_spans.TryTake(out _)) { } }

        public ISpan BeginSpan(string name, string? parentId = null)
        {
            var record = new SpanRecord { Name = name, ParentId = parentId };
            if (_spans.Count >= MaxSpans)
            {
                var oldest = _spans.OrderBy(s => s.StartTime).FirstOrDefault();
                if (oldest != null) _spans.Take(1);
            }
            _spans.Add(record);
            return new InMemorySpan(record, this);
        }

        private void EndSpan(SpanRecord record) => record.EndTime = DateTime.UtcNow;

        private sealed class InMemorySpan : ISpan
        {
            private readonly SpanRecord _record;
            private readonly InMemorySpanTracer _tracer;
            private bool _disposed;

            public string SpanId => _record.SpanId;
            public string Name => _record.Name;

            public InMemorySpan(SpanRecord record, InMemorySpanTracer tracer)
            {
                _record = record;
                _tracer = tracer;
            }

            public void SetAttribute(string key, object value) => _record.Attributes[key] = value;
            public void RecordException(Exception ex) => _record.Exception = ex;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _tracer.EndSpan(_record);
            }
        }
    }
}
