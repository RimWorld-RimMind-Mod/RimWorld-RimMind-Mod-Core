using System;
using System.IO;
using RimMind.Application.Common.Models.Debug;
using RimMind.Infrastructure.UI.AgentsPage;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.UI.Layout;
using RimMind.Testing;
using UnityEngine;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    [Collection("UI drawing")]
    public sealed class Batch3CoreUiPerfContracts : IDisposable
    {
        private readonly Event? _savedEvent = Event.current;
        private readonly bool _savedShowOverlay = LayoutConflictStore.ShowOverlay;
        private readonly bool _savedTracingEnabled = LayoutConflictStore.TracingEnabled;
        private readonly int _savedOverlayFrames = LayoutConflictStore.OverlayFrameRemaining;

        public void Dispose()
        {
            Event.current = _savedEvent;
            LayoutConflictStore.ShowOverlay = _savedShowOverlay;
            LayoutConflictStore.TracingEnabled = _savedTracingEnabled;
            LayoutConflictStore.OverlayFrameRemaining = _savedOverlayFrames;
            LayoutConflictStore.Clear();
            Widgets.ResetDrawing();
        }

        [Fact]
        public void Layout_tracing_and_agent_trace_caching_avoid_per_frame_allocations()
        {
            ContractCaseRunner.Run(
                ("layout scope is a zero-allocation no-op when tracing and overlay are disabled", () =>
                {
                    LayoutConflictStore.Clear();
                    LayoutConflictStore.ShowOverlay = false;
                    LayoutConflictStore.TracingEnabled = false;
                    LayoutConflictStore.OverlayFrameRemaining = 0;
                    Event.current = new Event { type = EventType.Repaint };

                    RimMindLayoutScope scope1;
                    using (scope1 = RimMindLayoutScope.Begin("TestWindow", new Rect(0f, 0f, 400f, 300f)))
                    {
                        Assert.False(scope1.IsRecording);
                        scope1.Record(new Rect(0f, 0f, 100f, 100f), "A");
                        scope1.Record(new Rect(50f, 50f, 100f, 100f), "B");
                        Assert.Empty(scope1.Recorder.Entries);
                    }

                    Assert.False(LayoutConflictStore.TryGet("TestWindow", out _));

                    using var scope2 = RimMindLayoutScope.Begin("TestWindow", new Rect(0f, 0f, 400f, 300f));
                    Assert.Same(scope1, scope2);
                }),
                ("layout scope only records on Repaint events when tracing or overlay is enabled", () =>
                {
                    LayoutConflictStore.Clear();
                    LayoutConflictStore.TracingEnabled = true;

                    Event.current = new Event { type = EventType.Layout };
                    using (var layoutScope = RimMindLayoutScope.Begin("RepaintOnlyWin", new Rect(0f, 0f, 300f, 200f)))
                    {
                        Assert.False(layoutScope.IsRecording);
                        layoutScope.Record(new Rect(10f, 10f, 100f, 100f), "A");
                        layoutScope.Record(new Rect(20f, 20f, 100f, 100f), "B");
                    }
                    Assert.False(LayoutConflictStore.TryGet("RepaintOnlyWin", out _));

                    Event.current = new Event { type = EventType.Repaint };
                    LayoutTraceRecorder recorderRef;
                    using (var repaintScope = RimMindLayoutScope.Begin("RepaintOnlyWin", new Rect(0f, 0f, 300f, 200f)))
                    {
                        Assert.True(repaintScope.IsRecording);
                        recorderRef = repaintScope.Recorder;
                        repaintScope.Record(new Rect(10f, 10f, 100f, 100f), "A");
                        repaintScope.Record(new Rect(20f, 20f, 100f, 100f), "B");
                    }

                    Assert.True(LayoutConflictStore.TryGet("RepaintOnlyWin", out LayoutReport? report));
                    Assert.NotNull(report);
                    Assert.Single(report!.Conflicts);
                    Assert.Equal(ConflictKind.Overlap, report.Conflicts[0].Kind);

                    using (var secondRepaint = RimMindLayoutScope.Begin("RepaintOnlyWin", new Rect(0f, 0f, 500f, 400f)))
                    {
                        Assert.Same(recorderRef, secondRepaint.Recorder);
                        Assert.Equal(500f, secondRepaint.Recorder.ViewRect.width);
                        Assert.Empty(secondRepaint.Recorder.Entries);
                    }
                }),
                ("agent detail panel caches trace rows by revision and filters by selected pawn", () =>
                {
                    var log = new AIRequestTraceLog();
                    log.StartRequest("req-1", "mod:RimMind-Dialogue | npc:Pawn_101", "deepseek-chat", "sys", "prompt 1", "");
                    log.CompleteRequest("req-1", "{\"reply\":\"Hello from 101\"}", 42, 120);

                    log.StartRequest("req-2", "mod:RimMind-Dialogue | npc:Pawn_202", "deepseek-chat", "sys", "prompt 2", "");
                    log.CompleteRequest("req-2", "{\"reply\":\"Hello from 202\"}", 55, 140);

                    var drawer = new AgentDetailPanelDrawer(log);
                    var pawn101 = new Pawn { thingIDNumber = 101 };
                    var pawn202 = new Pawn { thingIDNumber = 202 };

                    var vm1First = drawer.BuildViewModel(pawn101);
                    var vm1Second = drawer.BuildViewModel(pawn101);

                    Assert.NotNull(vm1First);
                    Assert.NotNull(vm1Second);
                    Assert.Single(vm1First!.TraceRows);
                    Assert.Same(vm1First.TraceRows[0], vm1Second!.TraceRows[0]);
                    Assert.Equal(1, drawer.TraceRebuildCount);
                    Assert.Equal(0, log.EntriesSnapshotCount);
                    Assert.Contains("Hello from 101", vm1First.TraceRows[0].Summary);

                    var vm2 = drawer.BuildViewModel(pawn202);
                    Assert.NotNull(vm2);
                    Assert.Equal(2, drawer.TraceRebuildCount);
                    Assert.Single(vm2!.TraceRows);
                    Assert.NotSame(vm1First.TraceRows[0], vm2.TraceRows[0]);
                    Assert.Contains("Hello from 202", vm2.TraceRows[0].Summary);

                    var vm2Cached = drawer.BuildViewModel(pawn202);
                    Assert.Equal(2, drawer.TraceRebuildCount);
                    Assert.Same(vm2.TraceRows[0], vm2Cached!.TraceRows[0]);

                    log.StartRequest("req-3", "mod:RimMind-Dialogue | npc:Pawn_202", "deepseek-chat", "sys", "prompt 3", "");
                    log.CompleteRequest("req-3", "{\"reply\":\"Updated 202\"}", 60, 90);

                    var vm2AfterUpdate = drawer.BuildViewModel(pawn202);
                    Assert.Equal(3, drawer.TraceRebuildCount);
                    Assert.Equal(2, vm2AfterUpdate!.TraceRows.Count);
                    Assert.Contains("Updated 202", vm2AfterUpdate.TraceRows[0].Summary);
                    Assert.Equal(0, log.EntriesSnapshotCount);
                }),
                ("table drawer guards translations and renders headers safely", () =>
                {
                    Widgets.ResetDrawing();
                    var drawer = new RimMindTableDrawer();
                    var model = new DebugTableModel("TestTable", new[]
                    {
                        DebugTableRow.Create("r-1", DebugTableStatus.Completed, "12:00", "Pawn", "Colonist", "Chat", "gpt-4o", "ok", "10ms")
                    });
                    var scroll = new Vector2();
                    using var scope = RimMindLayoutScope.Begin("TableWin", new Rect(0f, 0f, 600f, 300f));
                    drawer.Draw(new Rect(0f, 0f, 600f, 300f), model, ref scroll, scope);

                    Assert.Contains(Widgets.Draws, d => d.Kind == "Label" && d.Label == "RimMind.UI.DebugTable.Header.Id");
                    Assert.Contains(Widgets.Draws, d => d.Kind == "LabelEllipses" && d.Label == "RimMind.UI.DebugTable.Status.Completed");
                }));
        }

        [Fact]
        public void Core_ui_drawers_and_log_sink_enforce_batch3_invariants()
        {
            ContractCaseRunner.Run(
                ("queue tab drawer reuses captured depths and caps request rows", () =>
                {
                    string queueSource = ReadSource("Presentation/UI/QueueTabDrawer.cs");
                    Assert.Equal(1, CountOccurrences(queueSource, "GetAllQueueDepths()"));
                    Assert.Contains("MaxDisplayedRequests = 50", queueSource, StringComparison.Ordinal);
                    Assert.Contains("FormatMoreRequests", queueSource, StringComparison.Ordinal);
                    Assert.Contains("_lastMeasuredHeight = listing.CurHeight", queueSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("Settings:Queue:Content", queueSource, StringComparison.Ordinal);
                }),
                ("api tab drawer scales height with backup endpoints, resets secrets, and removes inline duplicate tips", () =>
                {
                    string apiSource = ReadSource("Presentation/UI/ApiTabDrawer.cs");
                    string testConnSource = ReadSource("Presentation/UI/ApiTabDrawer.TestConnection.cs");
                    string settingsWinSource = ReadSource("Infrastructure/UI/Window_RimMindSettings.cs");

                    Assert.Contains("backupCount * 56f + 120f", testConnSource, StringComparison.Ordinal);
                    Assert.Contains("_lastMeasuredHeight = listing.CurHeight", apiSource, StringComparison.Ordinal);
                    Assert.Contains("ResetSecretVisibility()", apiSource, StringComparison.Ordinal);
                    Assert.Contains("ApiTabDrawer.ResetSecretVisibility()", settingsWinSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("Widgets.Label(text1, \"RimMind.Settings.ModelService.ActiveDesc\"", apiSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("listing.LabelWithTooltip(\"RimMind.Settings.QueueSeeTab\"", apiSource, StringComparison.Ordinal);
                    Assert.Contains("RimMind.Settings.Player2.BalanceJoules", apiSource, StringComparison.Ordinal);
                }),
                ("remote sync disables manual pull and push while an operation is active", () =>
                {
                    string syncSource = ReadSource("Presentation/UI/RemoteSyncSettingsUI.cs");
                    Assert.Contains("bool canRunManualSync = isConfigured && _activeOperation == null;", syncSource, StringComparison.Ordinal);
                    Assert.Contains("GUI.enabled = prevEnabled && canRunManualSync;", syncSource, StringComparison.Ordinal);
                    Assert.Contains("if (_activeOperation != null) return;", syncSource, StringComparison.Ordinal);
                }),
                ("request overlay caches and guards per-frame translations", () =>
                {
                    string overlaySource = ReadSource("Infrastructure/UI/RequestOverlay.cs");
                    Assert.Contains("DragResizeLabel", overlaySource, StringComparison.Ordinal);
                    Assert.Contains("EmptyLabel", overlaySource, StringComparison.Ordinal);
                    Assert.Contains("TitleLabel", overlaySource, StringComparison.Ordinal);
                    Assert.Contains("DetailsLabel", overlaySource, StringComparison.Ordinal);
                    Assert.Contains("FormatSystemBlocked", overlaySource, StringComparison.Ordinal);
                    Assert.Contains(".CanTranslate()", overlaySource, StringComparison.Ordinal);
                }),
                ("verse log sink preserves RimWorld log spam protection", () =>
                {
                    string logSinkSource = ReadSource("Infrastructure/Services/Verse/VerseLogSink.cs");
                    Assert.DoesNotContain("ResetMessageCount", logSinkSource, StringComparison.Ordinal);
                }),
                ("english and chinese keyed xml files maintain parity for new keys", () =>
                {
                    foreach (string language in new[] { "English", "ChineseSimplified" })
                    {
                        string xml = File.ReadAllText(Path.Combine(
                            CoreRoot(),
                            "Languages",
                            language,
                            "Keyed",
                            "RimMind_Core.xml"));
                        Assert.Contains("<RimMind.Settings.Player2.BalanceJoules>", xml, StringComparison.Ordinal);
                        Assert.Contains("<RimMind.Settings.Queue.MoreRequests>", xml, StringComparison.Ordinal);
                    }
                }));
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(CoreRoot(), "Source", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string CoreRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core");
        }
    }
}
