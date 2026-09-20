using System;
using System.IO;
using System.Threading.Tasks;
using RimMind.Infrastructure.UI;
using RimMind.Infrastructure.UI.Layout;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Xunit;

namespace RimMind.Tests.Contracts;

[Collection("UI drawing")]
public sealed class UiCaptureContract : IDisposable
{
    private readonly bool _enabled = GUI.enabled;
    private readonly Event? _event = Event.current;
    private readonly int _frame = Time.frameCount;

    public void Dispose()
    {
        GUI.enabled = _enabled;
        Event.current = _event;
        Time.frameCount = _frame;
    }

    [Fact]
    public async Task Artifacts_are_fresh_and_cannot_overwrite_or_escape_the_run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RimMind-capture-test-" + Guid.NewGuid().ToString("N"));
        string runId = Guid.NewGuid().ToString("N");
        var artifacts = new UiCaptureArtifacts(root, runId);
        byte[] bytes = { 1, 2, 3 };
        Assert.Equal(bytes, await artifacts.WriteImageAsync("settings.png", bytes));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(artifacts.DirectoryPath, "settings.png")));
        await Assert.ThrowsAsync<IOException>(() => artifacts.WriteImageAsync("settings.png", new byte[] { 9 }));
        await Assert.ThrowsAsync<ArgumentException>(() => artifacts.WriteImageAsync("../escape.png", bytes));
        Assert.Throws<IOException>(() => new UiCaptureArtifacts(root, runId));
        Assert.Throws<ArgumentException>(() => new UiCaptureArtifacts(root, "../escape"));
        await artifacts.WriteManifestAsync("{\"Status\":\"failed\"}");
        Assert.Contains("failed", File.ReadAllText(Path.Combine(artifacts.DirectoryPath, "manifest.json")));
    }

    [Fact]
    public void Only_completed_repaints_count_and_capture_preserves_normal_control_appearance()
    {
        var window = new CaptureProbe { IsOpen = true, CaptureReadOnly = true };
        GUI.enabled = true;
        Time.frameCount = 25;
        Event.current = new Event { type = EventType.Layout };
        window.DoWindowContents(new Rect(0, 0, 300, 200));
        Assert.Equal(-1, window.LastRepaintFrame);
        Assert.True(window.WasEnabled);
        Assert.True(GUI.enabled);
        Event.current.type = EventType.Repaint;
        window.DoWindowContents(new Rect(0, 0, 300, 200));
        Assert.Equal(25, window.LastRepaintFrame);
        Assert.True(window.WasEnabled);
        window.Throw = true;
        Time.frameCount = 26;
        Assert.Throws<InvalidOperationException>(() => window.DoWindowContents(new Rect(0, 0, 300, 200)));
        Assert.Equal(25, window.LastRepaintFrame);
        Assert.True(GUI.enabled);
    }

    [Theory]
    [InlineData(EventType.MouseDown)]
    [InlineData(EventType.MouseUp)]
    [InlineData(EventType.KeyDown)]
    [InlineData(EventType.ScrollWheel)]
    public void Capture_blocks_input_without_blocking_normal_windows(EventType input)
    {
        var window = new CaptureProbe { IsOpen = true, CaptureReadOnly = true };
        Event.current = new Event { type = input };
        window.DoWindowContents(new Rect(0, 0, 300, 200));
        Assert.Equal(0, window.DrawCount);
        Assert.Equal(-1, window.LastRepaintFrame);

        window.CaptureReadOnly = false;
        window.DoWindowContents(new Rect(0, 0, 300, 200));
        Assert.Equal(1, window.DrawCount);
    }

    private sealed class CaptureProbe : RimMindWindowBase
    {
        public bool WasEnabled;
        public bool Throw;
        public int DrawCount;
        protected override void DrawContents(Rect rect, RimMindLayoutScope scope)
        {
            DrawCount++;
            WasEnabled = GUI.enabled;
            if (Throw) throw new InvalidOperationException("draw failed");
        }
    }

    [Fact]
    public void Two_distinct_repaints_are_required_before_capture()
    {
        var sequence = new UiCaptureSequence(new[] { "settings" }, 0);
        sequence.ObserveRepaint("settings", 10, 1);
        sequence.ObserveRepaint("settings", 10, 2);
        Assert.False(sequence.TryBeginCapture(2));
        sequence.ObserveRepaint("settings", 11, 3);
        Assert.True(sequence.TryBeginCapture(3));
        Assert.False(sequence.TryBeginCapture(3));
        Assert.False(sequence.IsComplete);
        sequence.ConfirmCapture("settings", true, 4);
        Assert.True(sequence.IsComplete);
    }

    [Fact]
    public void Next_page_needs_new_render_evidence_and_its_own_verified_image()
    {
        var sequence = new UiCaptureSequence(new[] { "overview", "settings" }, 0);
        sequence.ObserveRepaint("overview", 10, 1);
        sequence.ObserveRepaint("overview", 11, 2);
        Assert.True(sequence.TryBeginCapture(2));
        sequence.ConfirmCapture("overview", true, 3);
        Assert.Equal("settings", sequence.CurrentSceneId);
        Assert.False(sequence.TryBeginCapture(3));
        sequence.ObserveRepaint("settings", 12, 4);
        sequence.ObserveRepaint("settings", 13, 5);
        Assert.True(sequence.TryBeginCapture(5));
        sequence.ConfirmCapture("settings", false, 6);
        Assert.Equal("invalid-image", sequence.Error);
        Assert.False(sequence.IsComplete);
        Assert.False(sequence.TryBeginCapture(7));
    }

    [Fact]
    public void Wrong_page_evidence_cannot_be_attributed_to_requested_page()
    {
        var sequence = new UiCaptureSequence(new[] { "settings" }, 0);
        sequence.ObserveRepaint("overview", 10, 1);
        Assert.Equal("wrong-page", sequence.Error);
        sequence.ObserveRepaint("settings", 11, 2);
        sequence.ObserveRepaint("settings", 12, 3);
        Assert.False(sequence.TryBeginCapture(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ten_second_stall_terminates_render_or_file_wait(bool fileWait)
    {
        var sequence = new UiCaptureSequence(new[] { "settings" }, 0);
        if (fileWait)
        {
            sequence.ObserveRepaint("settings", 1, 0);
            sequence.ObserveRepaint("settings", 2, 1);
            Assert.True(sequence.TryBeginCapture(1));
        }
        sequence.CheckTimeout(fileWait ? 11 : 10);
        Assert.Equal("timeout", sequence.Error);
        Assert.False(sequence.IsComplete);
    }

    [Fact]
    public void Cancellation_and_wrong_capture_receipts_never_advance()
    {
        var cancelled = new UiCaptureSequence(new[] { "settings" }, 0);
        cancelled.Cancel();
        cancelled.ConfirmCapture("settings", true, 1);
        Assert.True(cancelled.IsCancelled);
        Assert.False(cancelled.IsComplete);

        var waiting = new UiCaptureSequence(new[] { "settings" }, 0);
        waiting.ConfirmCapture("settings", true, 1);
        Assert.Equal("unexpected-capture", waiting.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    public void Invalid_scene_ids_are_rejected(string id)
        => Assert.Throws<ArgumentException>(() => new UiCaptureSequence(new[] { id }, 0));

    [Fact]
    public void Empty_or_duplicate_scene_lists_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new UiCaptureSequence(Array.Empty<string>(), 0));
        Assert.Throws<ArgumentException>(() => new UiCaptureSequence(new[] { "settings", "settings" }, 0));
    }
}
