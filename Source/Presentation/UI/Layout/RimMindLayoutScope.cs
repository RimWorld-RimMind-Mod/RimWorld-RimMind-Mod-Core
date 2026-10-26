using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI.Layout
{
    /// <summary>
    /// Disposable frame scope wrapping a LayoutTraceRecorder.
    /// On Dispose, runs conflict detection and publishes to LayoutConflictStore.
    /// Usage:
    ///   using (var scope = RimMindLayoutScope.Begin(nameof(MyWindow), inRect))
    ///   {
    ///       scope.Record(someRect, "label");
    ///       ...
    ///   }
    /// </summary>
    public sealed class RimMindLayoutScope : System.IDisposable
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LayoutTraceRecorder> Recorders = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RimMindLayoutScope> NoOpScopes = new();
        private static readonly LayoutTraceRecorder NoOpRecorder = new(default);

        public string WindowName { get; }
        public LayoutTraceRecorder Recorder { get; }
        public bool IsRecording { get; }
        public bool AutoPublish { get; set; } = true;

        private RimMindLayoutScope(string windowName, LayoutTraceRecorder recorder, bool isRecording)
        {
            WindowName = windowName;
            Recorder = recorder;
            IsRecording = isRecording;
        }

        public static bool ShouldRecord()
            => LayoutConflictStore.IsTracingActive
               && (Event.current == null || Event.current.type == EventType.Repaint);

        public static RimMindLayoutScope Begin(string windowName, Rect viewRect)
        {
            string key = windowName ?? string.Empty;
            if (!ShouldRecord())
            {
                var noOp = NoOpScopes.GetOrAdd(key, static name => new RimMindLayoutScope(name, NoOpRecorder, isRecording: false));
                noOp.AutoPublish = true;
                return noOp;
            }

            var recorder = Recorders.GetOrAdd(key, static _ => new LayoutTraceRecorder(default));
            recorder.Reset(viewRect);
            return new RimMindLayoutScope(key, recorder, isRecording: true);
        }

        public void Record(Rect rect, string label = "", [System.Runtime.CompilerServices.CallerMemberName] string source = "")
        {
            if (!IsRecording) return;
            Recorder.Record(rect, label, source);
        }

        public void Dispose()
        {
            if (!IsRecording || !AutoPublish) return;
            var conflicts = Recorder.DetectConflicts();
            var report = new LayoutReport(WindowName, conflicts);
            LayoutConflictStore.Publish(report);

            bool drawOverlay = LayoutConflictStore.ShowOverlay || LayoutConflictStore.OverlayFrameRemaining > 0;
            if (LayoutConflictStore.OverlayFrameRemaining > 0)
                LayoutConflictStore.OverlayFrameRemaining--;

            if (drawOverlay && conflicts.Count > 0)
            {
                foreach (var c in conflicts)
                {
                    foreach (var entry in c.Entries)
                    {
                        var color = c.Kind switch
                        {
                            ConflictKind.Overlap => new Color(1f, 0.4f, 0.4f, 0.6f),
                            ConflictKind.Overflow => new Color(1f, 0.7f, 0.2f, 0.6f),
                            ConflictKind.NegativeSize => new Color(0.8f, 0.2f, 1f, 0.6f),
                            _ => Color.red
                        };
                        var oldColor = GUI.color;
                        GUI.color = color;
                        Widgets.DrawBox(entry.Rect, 2);
                        GUI.color = oldColor;
                    }
                }
            }
        }
    }
}
