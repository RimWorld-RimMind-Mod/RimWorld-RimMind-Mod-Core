using RimMind.Presentation.Runtime.Services;

namespace RimMind.Infrastructure.UI.DebugCenter.Overview
{
    public sealed class DebugCenterOverviewModel
    {
        public DebugCenterOverviewModel(
            int activeAgents,
            int pausedAgents,
            int dormantAgents,
            int terminatedAgents,
            int pendingRequests,
            string queueState,
            string selectedObject,
            int registeredPawnAgents,
            int registeredScopedAgents,
            int lastAgentLoopTick,
            int agentLoopFaults,
            RuntimeLifecycleDiagnostics? runtimeDiagnostics = null,
            GameLifecycleDiagnostics? gameDiagnostics = null)
        {
            ActiveAgents = activeAgents;
            PausedAgents = pausedAgents;
            DormantAgents = dormantAgents;
            TerminatedAgents = terminatedAgents;
            PendingRequests = pendingRequests;
            QueueState = queueState ?? string.Empty;
            SelectedObject = selectedObject ?? string.Empty;
            RegisteredPawnAgents = registeredPawnAgents;
            RegisteredScopedAgents = registeredScopedAgents;
            LastAgentLoopTick = lastAgentLoopTick;
            AgentLoopFaults = agentLoopFaults;
            RuntimeDiagnostics = runtimeDiagnostics;
            GameDiagnostics = gameDiagnostics;
        }

        public int ActiveAgents { get; }

        public int PausedAgents { get; }

        public int DormantAgents { get; }

        public int TerminatedAgents { get; }

        public int PendingRequests { get; }

        public string QueueState { get; }

        public string SelectedObject { get; }

        public int RegisteredPawnAgents { get; }

        public int RegisteredScopedAgents { get; }

        public int LastAgentLoopTick { get; }

        public int AgentLoopFaults { get; }

        public RuntimeLifecycleDiagnostics? RuntimeDiagnostics { get; private set; }

        public GameLifecycleDiagnostics? GameDiagnostics { get; private set; }

        public void AttachLifecycleDiagnostics(
            RuntimeLifecycleDiagnostics? runtimeDiagnostics,
            GameLifecycleDiagnostics? gameDiagnostics)
        {
            RuntimeDiagnostics = runtimeDiagnostics;
            GameDiagnostics = gameDiagnostics;
        }

        public long RuntimeGeneration => RuntimeDiagnostics?.Generation ?? 0;

        public int RuntimeServiceCount => RuntimeDiagnostics?.ServiceCount ?? 0;

        public System.DateTimeOffset? RuntimePublishedAtUtc => RuntimeDiagnostics?.PublishedAtUtc;

        public System.Guid RuntimeId => RuntimeDiagnostics?.RuntimeId ?? System.Guid.Empty;

        public string? LastBuildFailureSummary => RuntimeDiagnostics?.LastBuildFailureSummary;

        public long StaleCompletionDiscardCount => RuntimeDiagnostics?.StaleCompletionDiscardCount ?? 0;

        public long GameGeneration => GameDiagnostics?.Generation ?? 0;

        public int GameServiceCount => GameDiagnostics?.ServiceCount ?? 0;

        public System.DateTimeOffset? GamePublishedAtUtc => GameDiagnostics?.PublishedAtUtc;

        public string AgentLoopSummary => $"{RegisteredPawnAgents} pawn / {RegisteredScopedAgents} scoped";

        public static DebugCenterOverviewModel SnapshotFixture()
            => new(
                activeAgents: 3,
                pausedAgents: 1,
                dormantAgents: 2,
                terminatedAgents: 0,
                pendingRequests: 4,
                queueState: "Queue: Running",
                selectedObject: "Nickie",
                registeredPawnAgents: 3,
                registeredScopedAgents: 1,
                lastAgentLoopTick: 900,
                agentLoopFaults: 0);
    }
}
