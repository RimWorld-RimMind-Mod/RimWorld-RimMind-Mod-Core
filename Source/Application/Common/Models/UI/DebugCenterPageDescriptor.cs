namespace RimMind.Application.Common.Models.UI
{
    public sealed record DebugCenterPageDescriptor(
        string Id,
        string LabelKey,
        int Order,
        bool IsDefault);
}
