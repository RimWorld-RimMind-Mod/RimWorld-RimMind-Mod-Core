namespace RimMind.Application.Common.Interfaces.Abstractions
{
    public interface IThreadChecker
    {
        bool IsMainThread { get; }
    }
}
