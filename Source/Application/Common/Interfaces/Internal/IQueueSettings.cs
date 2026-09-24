namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IQueueSettings
    {
        int QueueProcessInterval { get; set; }
        int MaxConcurrentRequests { get; set; }
        int RequestTimeoutMs { get; set; }
        int MaxRetryCount { get; set; }
        int RequestExpireTicks { get; set; }
    }
}
