using System;

namespace RimMind.Application.Common.Interfaces.Diagnostics
{
    public interface ISpan : IDisposable
    {
        string SpanId { get; }
        string Name { get; }
        void SetAttribute(string key, object value);
        void RecordException(Exception ex);
    }
}
