namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismEnumResult
    {
        public string DefName { get; init; } = "";
        public string Label { get; init; } = "";
        public string? Description { get; init; }
    }
}
