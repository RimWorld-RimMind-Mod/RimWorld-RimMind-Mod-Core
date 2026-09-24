using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Presentation.UI.Layout
{
    /// <summary>
    /// Immutable snapshot of one window's layout conflicts for one frame.
    /// </summary>
    public sealed class LayoutReport
    {
        public string WindowName { get; }
        public IReadOnlyList<LayoutConflict> Conflicts { get; }
        public int FrameNumber { get; }

        public LayoutReport(string windowName, IReadOnlyList<LayoutConflict> conflicts, int frameNumber = 0)
        {
            WindowName = windowName;
            Conflicts = conflicts;
            FrameNumber = frameNumber;
        }

        public bool HasConflicts => Conflicts.Count > 0;
    }

    /// <summary>
    /// Static per-window conflict cache. Thread-safe (ConcurrentDictionary).
    /// Bounded: only the latest report per window name is retained.
    /// </summary>
    public static class LayoutConflictStore
    {
        private static readonly ConcurrentDictionary<string, LayoutReport> _reports = new();

        public static void Publish(LayoutReport report)
            => _reports[report.WindowName] = report;

        public static bool TryGet(string windowName, out LayoutReport? report)
            => _reports.TryGetValue(windowName, out report);

        public static IEnumerable<LayoutReport> GetAll() => _reports.Values;

        /// <summary>
        /// Returns the report with the most conflicts, or null if store is empty.
        /// Ties broken alphabetically by window name for determinism.
        /// </summary>
        public static LayoutReport? GetWorst()
            => _reports.Values
                .OrderByDescending(r => r.Conflicts.Count)
                .ThenBy(r => r.WindowName)
                .FirstOrDefault();

        public static void Clear() => _reports.Clear();

        /// <summary>
        /// When true, RimMindLayoutScope.Dispose draws colored boxes around
        /// conflicting rects for one frame. Toggled by debug action.
        /// </summary>
        public static bool ShowOverlay { get; set; }

        public static int OverlayFrameRemaining { get; set; }
    }
}
