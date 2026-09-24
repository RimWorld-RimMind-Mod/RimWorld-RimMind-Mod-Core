namespace RimMind.Application.Common.Interfaces.Diagnostics
{
    public interface ISpanTracer
    {
        ISpan BeginSpan(string name, string? parentId = null);
    }
}
