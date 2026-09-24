using System.Collections.Generic;
using UnityEngine;

namespace RimMind.Presentation.UI.Layout
{
    /// <summary>
    /// Accumulates LayoutTraceEntry records for one draw frame and detects
    /// overlaps, overflow, and negative-size rects. Pure logic: no Unity GUI calls.
    /// </summary>
    public sealed class LayoutTraceRecorder
    {
        // Sub-pixel tolerance for overflow detection. RimWorld GUI floats often drift by <0.5f.
        private const float OverflowEpsilon = 0.5f;

        private readonly List<LayoutTraceEntry> _entries = new();

        public Rect ViewRect { get; }

        public LayoutTraceRecorder(Rect viewRect)
        {
            ViewRect = viewRect;
        }

        public void Record(Rect rect, string label, string source)
            => _entries.Add(new LayoutTraceEntry(rect, label, source));

        public void Reset() => _entries.Clear();

        public IReadOnlyList<LayoutTraceEntry> Entries => _entries;

        /// <summary>
        /// Run all three detectors and return a list of conflicts.
        /// Order: NegativeSize first (data error), then Overflow, then Overlap.
        /// </summary>
        public List<LayoutConflict> DetectConflicts()
        {
            var result = new List<LayoutConflict>();

            // 1. Negative size
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Rect.width < 0f || e.Rect.height < 0f)
                    result.Add(LayoutConflict.NegativeSize(e));
            }

            // 2. Overflow
            float viewBottom = ViewRect.yMax;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Rect.width < 0f || e.Rect.height < 0f) continue; // already flagged
                float entryBottom = e.Rect.yMax;
                if (entryBottom > viewBottom + OverflowEpsilon)
                    result.Add(LayoutConflict.Overflow(e, ViewRect, entryBottom));
            }

            // 3. Overlap (O(n²) — fine for <200 entries per frame)
            for (int i = 0; i < _entries.Count; i++)
            {
                var a = _entries[i];
                if (a.Rect.width < 0f || a.Rect.height < 0f) continue;
                for (int j = i + 1; j < _entries.Count; j++)
                {
                    var b = _entries[j];
                    if (b.Rect.width < 0f || b.Rect.height < 0f) continue;
                    if (InteriorsIntersect(a.Rect, b.Rect) &&
                        !ContainsRect(a.Rect, b.Rect) &&
                        !ContainsRect(b.Rect, a.Rect))
                        result.Add(LayoutConflict.Overlap(a, b));
                }
            }

            return result;
        }

        /// <summary>
        /// Interior intersection test — touching edges do NOT count as overlap.
        /// Returns true only if both X and Y ranges have strictly positive overlap.
        /// </summary>
        private static bool InteriorsIntersect(Rect a, Rect b)
        {
            float xOverlap = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float yOverlap = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return xOverlap > OverflowEpsilon && yOverlap > OverflowEpsilon;
        }

        private static bool ContainsRect(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - OverflowEpsilon &&
                   inner.yMin >= outer.yMin - OverflowEpsilon &&
                   inner.xMax <= outer.xMax + OverflowEpsilon &&
                   inner.yMax <= outer.yMax + OverflowEpsilon;
        }
    }
}
