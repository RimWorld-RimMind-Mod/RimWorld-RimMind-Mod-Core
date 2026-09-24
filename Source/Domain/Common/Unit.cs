using System;

namespace RimMind.Domain.Common
{
    /// <summary>
    /// Represents a void return type for Result patterns.
    /// Used when an operation returns success/failure but no meaningful value.
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Value = new();

        public override string ToString() => "()";
        public override int GetHashCode() => 0;
        public override bool Equals(object? obj) => obj is Unit;
        public bool Equals(Unit other) => true;

        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
