using System;

namespace RimMind.Presentation.Runtime.Services
{
    public enum RuntimeLifecycleState
    {
        NeverPublished,
        Building,
        Running,
        Stopped,
        Failed
    }

    public enum GameLifecycleState
    {
        NeverPublished,
        Running,
        Stopped,
        Failed
    }

    public sealed class RuntimeLifecycleDiagnostics
    {
        public RuntimeLifecycleDiagnostics(
            RuntimeLifecycleState state,
            long generation,
            int serviceCount,
            Guid runtimeId,
            DateTimeOffset? publishedAtUtc,
            string? lastBuildFailureSummary,
            long staleCompletionDiscardCount)
        {
            State = state;
            Generation = generation;
            ServiceCount = serviceCount;
            RuntimeId = runtimeId;
            PublishedAtUtc = publishedAtUtc;
            LastBuildFailureSummary = lastBuildFailureSummary;
            StaleCompletionDiscardCount = staleCompletionDiscardCount;
        }

        public RuntimeLifecycleState State { get; }

        public long Generation { get; }

        public int ServiceCount { get; }

        public Guid RuntimeId { get; }

        public DateTimeOffset? PublishedAtUtc { get; }

        public string? LastBuildFailureSummary { get; }

        public long StaleCompletionDiscardCount { get; }
    }

    public sealed class GameLifecycleDiagnostics
    {
        public GameLifecycleDiagnostics(
            GameLifecycleState state,
            long generation,
            int serviceCount,
            DateTimeOffset? publishedAtUtc)
        {
            State = state;
            Generation = generation;
            ServiceCount = serviceCount;
            PublishedAtUtc = publishedAtUtc;
        }

        public GameLifecycleState State { get; }

        public long Generation { get; }

        public int ServiceCount { get; }

        public DateTimeOffset? PublishedAtUtc { get; }
    }
}
