namespace RimMind.Domain.Agent.Planning;

public sealed record ScheduleBlock
{
    public int StartHour { get; init; }
    public int DurationHours { get; init; }
    public string Activity { get; init; } = "";
    public string Reason { get; init; } = "";
}
