using System;
using UnityEngine;

namespace RimMind.Presentation.UI.Layout
{
    public enum ConflictKind
    {
        Overlap,
        Overflow,
        NegativeSize
    }

    public readonly struct LayoutTraceEntry : IEquatable<LayoutTraceEntry>
    {
        public Rect Rect { get; }
        public string Label { get; }
        public string Source { get; }

        public LayoutTraceEntry(Rect rect, string label, string source)
        {
            Rect = rect;
            Label = label ?? string.Empty;
            Source = source ?? string.Empty;
        }

        public bool Equals(LayoutTraceEntry other)
            => Rect.Equals(other.Rect) && Label == other.Label && Source == other.Source;
        public override bool Equals(object? obj) => obj is LayoutTraceEntry e && Equals(e);
        public override int GetHashCode() => System.HashCode.Combine(Rect, Label, Source);
    }

    public sealed class LayoutConflict
    {
        public ConflictKind Kind { get; }
        public LayoutTraceEntry[] Entries { get; }
        public Rect ViewRect { get; }
        public float OverflowBottom { get; }
        public string Message { get; }

        private LayoutConflict(ConflictKind kind, LayoutTraceEntry[] entries, Rect viewRect, float overflowBottom, string message)
        {
            Kind = kind;
            Entries = entries;
            ViewRect = viewRect;
            OverflowBottom = overflowBottom;
            Message = message ?? string.Empty;
        }

        public static LayoutConflict Overlap(LayoutTraceEntry a, LayoutTraceEntry b)
            => new(ConflictKind.Overlap, new[] { a, b }, default, 0f,
                $"Overlap: '{a.Label}' ({a.Source}) intersects '{b.Label}' ({b.Source})");

        public static LayoutConflict Overflow(LayoutTraceEntry entry, Rect viewRect, float overflowBottom)
            => new(ConflictKind.Overflow, new[] { entry }, viewRect, overflowBottom,
                $"Overflow: '{entry.Label}' ({entry.Source}) bottom={overflowBottom:F1} exceeds viewRect bottom={viewRect.yMax:F1}");

        public static LayoutConflict NegativeSize(LayoutTraceEntry entry)
            => new(ConflictKind.NegativeSize, new[] { entry }, default, 0f,
                $"NegativeSize: '{entry.Label}' ({entry.Source}) has w={entry.Rect.width:F1} h={entry.Rect.height:F1}");
    }
}
