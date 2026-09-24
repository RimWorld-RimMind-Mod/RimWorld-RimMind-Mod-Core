using System.Threading;

namespace RimMind.Application.Common.Interfaces.Async
{
    public interface ICompletionFence
    {
        CancellationToken CancellationToken { get; }

        bool TryAcceptCompletion();
    }
}
