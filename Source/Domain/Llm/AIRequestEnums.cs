namespace RimMind.Domain.Llm
{
    public enum AIRequestPriority
    {
        Low,
        Normal,
        High,
        Critical,
        Immediate
    }

    public enum AIRequestState
    {
        Queued,
        Processing,
        Completed,
        Error,
        Cancelled
    }
}
