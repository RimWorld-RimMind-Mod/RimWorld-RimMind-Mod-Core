namespace RimMind.Core.Client
{
    public enum AIRequestState
    {
        Queued = 0,
        Processing = 1,
        Completed = 2,
        Error = 3,
        Cancelled = 4
    }

    public enum AIRequestPriority
    {
        High = 0,
        Normal = 1,
        Low = 2
    }
}
