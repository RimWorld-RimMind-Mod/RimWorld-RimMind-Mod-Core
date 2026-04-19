namespace RimMind.Core.Client
{
    public enum AIRequestState
    {
        Queued,
        Processing,
        Completed,
        Error,
        Cancelled
    }

    public enum AIRequestPriority
    {
        High = 0,
        Normal = 1,
        Low = 2
    }
}
