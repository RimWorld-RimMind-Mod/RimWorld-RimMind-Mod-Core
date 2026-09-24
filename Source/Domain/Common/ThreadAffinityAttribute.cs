using System;

namespace RimMind.Domain.Common;

public enum ThreadAffinityKind
{
    MainOnly,
    BackgroundOnly,
    Any
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface)]
public class ThreadAffinityAttribute : Attribute
{
    public ThreadAffinityKind Kind { get; }

    public ThreadAffinityAttribute(ThreadAffinityKind kind)
    {
        Kind = kind;
    }
}
