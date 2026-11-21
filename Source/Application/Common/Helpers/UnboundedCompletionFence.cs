using System.Threading;
using RimMind.Application.Common.Interfaces.Async;

namespace RimMind.Application.Common.Helpers
{
    /// <summary>
    /// Shared default completion fence that never cancels and always accepts completions.
    /// Replaces duplicated private copies across RequestQueue and ProactiveBehaviorOrchestrator.
    /// </summary>
    public sealed class UnboundedCompletionFence : ICompletionFence
    {
        public static readonly UnboundedCompletionFence Instance = new UnboundedCompletionFence();

        private UnboundedCompletionFence() { }

        public CancellationToken CancellationToken => CancellationToken.None;

        public bool TryAcceptCompletion() => true;
    }
}
