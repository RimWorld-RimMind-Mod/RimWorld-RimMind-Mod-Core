using System;

namespace RimMind.Domain.Agent.Modes;

/// <summary>
/// Identifies an agent mode. Use predefined constants or create custom IDs for sub-mod modes.
/// </summary>
public readonly record struct AgentModeId : IComparable<AgentModeId>
{
    public string Value { get; init; }

    public AgentModeId(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    // Built-in mode IDs
    public static AgentModeId Reactive { get; } = new("rimmind.reactive");
    public static AgentModeId Proactive { get; } = new("rimmind.proactive");
    public static AgentModeId Dormant { get; } = new("rimmind.dormant");

    // Implicit conversion from string for convenience
    public static implicit operator AgentModeId(string value) => new(value);
    public static implicit operator string(AgentModeId id) => id.Value;

    public override string ToString() => Value;

    public int CompareTo(AgentModeId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator <(AgentModeId left, AgentModeId right) => left.CompareTo(right) < 0;
    public static bool operator >(AgentModeId left, AgentModeId right) => left.CompareTo(right) > 0;
    public static bool operator <=(AgentModeId left, AgentModeId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AgentModeId left, AgentModeId right) => left.CompareTo(right) >= 0;

    public static AgentModeId Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return default;
        var lower = raw.ToLowerInvariant();
        return lower switch
        {
            "reactive" => Reactive,
            "proactive" => Proactive,
            "dormant" => Dormant,
            _ => new AgentModeId(lower.Contains(".") ? lower : $"rimmind.{lower}")
        };
    }
}
