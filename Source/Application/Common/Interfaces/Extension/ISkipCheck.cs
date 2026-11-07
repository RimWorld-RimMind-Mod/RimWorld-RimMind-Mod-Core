using System;

namespace RimMind.Application.Common.Interfaces.Extension;

public enum SkipCheckKind
{
    Dialogue,
    FloatMenu,
    Action,
    StorytellerIncident
}

public static class SkipCheckChannels
{
    public const string Dialogue = "dialogue";
    public const string FloatMenu = "float_menu";
    public const string Action = "action";
    public const string StorytellerIncident = "storyteller_incident";

    public static string FromKind(SkipCheckKind kind) => kind switch
    {
        SkipCheckKind.Dialogue => Dialogue,
        SkipCheckKind.FloatMenu => FloatMenu,
        SkipCheckKind.Action => Action,
        SkipCheckKind.StorytellerIncident => StorytellerIncident,
        _ => string.Empty
    };

    public static string ResolveChannel(ISkipCheck? check)
    {
        if (check == null) return string.Empty;
        if (check is IChannelSkipCheck channelCheck && !string.IsNullOrWhiteSpace(channelCheck.Channel))
        {
            return channelCheck.Channel;
        }
        return FromKind(check.Kind);
    }

    public static bool Matches(ISkipCheck? check, string? channel)
    {
        if (check == null || string.IsNullOrWhiteSpace(channel))
            return false;
        return string.Equals(ResolveChannel(check), channel, StringComparison.OrdinalIgnoreCase);
    }
}

public readonly struct SkipCheckArgs
{
    private readonly string? _tag;

    public object? Pawn { get; init; }
    public object? RecipientPawn { get; init; }
    public int PawnId { get; init; }
    public int RecipientPawnId { get; init; }
    public string? Trigger { get; init; }
    public string? IntentId { get; init; }
    public string? Tag
    {
        get => _tag ?? Trigger ?? IntentId;
        init => _tag = value;
    }
}

public interface ISkipCheck : IExtension
{
    SkipCheckKind Kind { get; }
    bool ShouldSkip(in SkipCheckArgs args);
}

public interface IChannelSkipCheck : ISkipCheck
{
    string Channel { get; }
}
