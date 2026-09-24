namespace RimMind.Domain.ValueObjects
{
    public enum RimMindErrorCode
    {
        ClientNotConfigured = 1000,
        ClientTransientFailure = 1001,
        ClientPermanentFailure = 1002,
        ClientCircuitOpen = 1003,

        ContextBuildFailed = 2000,
        PipelineShortCircuited = 2001,

        ToolNotFound = 3000,
        ToolExecutionFailed = 3001,
        ToolPolicyDenied = 3002,
        ToolMaxDepthExceeded = 3003,
        MechanismOperationNotSupported = 3010,
        MechanismPawnNotFound = 3011,
        MechanismInvalidDefName = 3012,
        MechanismMapNotFound = 3013,
        MechanismInvalidAction = 3014,

        NpcNotFound = 4000,
        RemoteBackendFailed = 4001,

        InternalError = 9000,
        NotImplemented = 9001,
        Cancelled = 9002,
        Timeout = 9003,
    }
}
