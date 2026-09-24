namespace RimMind.Application.Features.Pipeline.Unified
{
    public sealed record GuardrailResult(bool Passed, string? Reason = null)
    {
        public static GuardrailResult Ok() => new(true);
        public static GuardrailResult Fail(string reason) => new(false, reason);
    }
}
