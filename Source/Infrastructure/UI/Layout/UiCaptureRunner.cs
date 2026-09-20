using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.Layout
{
    // Opt-in, short-lived MonoBehaviour: Update supplies the watchdog even if end-of-frame stalls.
    internal sealed class UiCaptureRunner : MonoBehaviour
    {
        private static UiCaptureRunner? _active;
        private static bool _startupChecked;
        private readonly Stopwatch _clock = new();
        private UiCaptureSequence _sequence = null!;
        private UiCaptureArtifacts _artifacts = null!;
        private IReadOnlyList<UiCaptureScene> _scenes = null!;
        private CaptureManifest _manifest = null!;
        private RimMindWindowBase? _window;
        private long _gameGeneration;
        private long _runtimeGeneration;
        private Task? _manifestWrite;
        private bool _finishing;

        internal static void CheckStartup()
        {
            if (_startupChecked || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            _startupChecked = true;
            if (GenCommandLine.TryGetCommandLineArg("rimmind-ui-capture", out string runId)) StartCapture(runId);
        }

        internal static void StartCapture(string? runId = null)
        {
            if (_active != null) { Log.Warning("[RimMind-Core] UI capture already running."); return; }
            if (!Prefs.DevMode || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                Log.Warning("[RimMind-Core] UI capture requires developer mode and a loaded map.");
                return;
            }
            UiCaptureRunner? runner = null;
            try
            {
                runner = Current.Root.gameObject.AddComponent<UiCaptureRunner>();
                _active = runner;
                runner.Initialize(runId ?? Guid.NewGuid().ToString("N"));
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Core] UI capture initialization failed: " + ex.GetType().Name);
                if (runner != null) Destroy(runner);
                _active = null;
            }
        }

        private void Initialize(string runId)
        {
            _artifacts = new UiCaptureArtifacts(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "UiCaptures"), runId);
            _scenes = UiCaptureScenes.Create();
            _gameGeneration = GameServiceHub.Shared.Generation;
            _runtimeGeneration = RuntimeServiceHub.Shared.Generation;
            _clock.Start();
            _sequence = new UiCaptureSequence(_scenes.Select(s => s.Id), 0);
            _manifest = new CaptureManifest
            {
                RunId = runId, CoreAssemblyMvid = typeof(UiCaptureRunner).Assembly.ManifestModule.ModuleVersionId.ToString(),
                GameAssemblyMvid = typeof(Game).Assembly.ManifestModule.ModuleVersionId.ToString(),
                GameVersion = VersionControl.CurrentVersionStringWithRev, Width = Screen.width, Height = Screen.height,
                UiScale = Prefs.UIScale, Language = LanguageDatabase.activeLanguage.folderName,
                ExpectedSceneIds = _scenes.Select(s => s.Id).ToArray()
            };
            Log.Message("[RimMind-Core] UI capture started: " + _artifacts.DirectoryPath);
            StartCoroutine(Run());
        }

        private void Update()
        {
            if (_sequence == null) return;
            if (_finishing)
            {
                if (_manifestWrite?.IsCompleted == true)
                {
                    Log.Message("[RimMind-Core] UI capture " + (_manifestWrite.IsFaulted ? "manifest-write-failed" : _manifest.Status)
                        + "; visual review not performed: " + _artifacts.DirectoryPath);
                    if (_manifestWrite.IsFaulted) _ = _manifestWrite.Exception;
                    Destroy(this);
                }
                return;
            }
            if (GameServiceHub.Shared.Generation != _gameGeneration || RuntimeServiceHub.Shared.Generation != _runtimeGeneration
                || Current.ProgramState != ProgramState.Playing)
                _sequence.Fail("lifecycle-changed");
            if (Input.GetKeyDown(KeyCode.Escape)) _sequence.Cancel();
            _sequence.CheckTimeout(_clock.Elapsed.TotalSeconds);
            if (_sequence.IsTerminal) Finish();
        }

        private IEnumerator Run()
        {
            foreach (UiCaptureScene scene in _scenes)
            {
                if (!Open(scene)) yield break;
                while (!_sequence.IsTerminal)
                {
                    yield return new WaitForEndOfFrame();
                    if (_finishing || !ValidateWindow(scene)) yield break;
                    _sequence.ObserveRepaint(scene.Id, _window!.LastRepaintFrame, _clock.Elapsed.TotalSeconds);
                    if (_sequence.TryBeginCapture(_clock.Elapsed.TotalSeconds)) break;
                }
                if (_sequence.IsTerminal) yield break;
                var receipt = new CaptureSceneReceipt { Id = scene.Id, PageId = ActualPageId(scene), Width = Screen.width, Height = Screen.height };
                var images = new List<byte[]>();
                for (int frame = 0; frame < scene.FrameCount; frame++)
                {
                    if (frame > 0) yield return new WaitForEndOfFrame();
                    if (_finishing || !ValidateWindow(scene)) yield break;
                    if (frame == 0) receipt.Frame = Time.frameCount;
                    else if (Time.frameCount != receipt.Frame + frame) { Abort("non-consecutive-frames"); yield break; }
                    byte[]? image = CaptureImage();
                    if (image == null) yield break;
                    images.Add(image);
                }

                Task<byte[]>[] writes = images.Select((bytes, index) => _artifacts.WriteImageAsync(
                    scene.Id + (index == 0 ? "" : "-" + index) + ".png", bytes)).ToArray();
                Task<byte[][]> allWrites = Task.WhenAll(writes);
                while (!allWrites.IsCompleted && !_sequence.IsTerminal) yield return null;
                if (allWrites.IsFaulted) { _ = allWrites.Exception; Abort("image-write-failed"); yield break; }
                if (_sequence.IsTerminal) yield break;
                bool valid = allWrites.Result.All(ValidateImage);
                _sequence.ConfirmCapture(scene.Id, valid, _clock.Elapsed.TotalSeconds);
                if (!valid) { Finish(); yield break; }
                receipt.File = scene.Id + ".png";
                receipt.AdditionalFiles = Enumerable.Range(1, images.Count - 1).Select(i => scene.Id + "-" + i + ".png").ToArray();
                receipt.AdditionalFrames = Enumerable.Range(1, images.Count - 1).Select(i => receipt.Frame + i).ToArray();
                receipt.Status = "captured";
                _manifest.Scenes.Add(receipt);
                CloseOwnedWindow();
            }
            Finish();
        }

        private bool Open(UiCaptureScene scene)
        {
            try
            {
                _window = scene.Open();
                _window.CaptureReadOnly = true;
                _window.forcePause = true;
                _window.closeOnClickedOutside = false;
                _window.doCloseX = false;
                _window.absorbInputAroundWindow = true;
                Find.WindowStack.Add(_window);
                return true;
            }
            catch (Exception ex) { Abort("scene-open-failed-" + ex.GetType().Name); return false; }
        }

        private string ActualPageId(UiCaptureScene scene)
            => _window is Window_RimMindHub hub ? hub.CurrentPageId : scene.PageId;

        private bool ValidateWindow(UiCaptureScene scene)
        {
            if (_window == null || !_window.IsOpen) { Abort("window-closed"); return false; }
            if (ActualPageId(scene) != scene.PageId) { Abort("wrong-page"); return false; }
            if (_window.LastRepaintFrame != Time.frameCount) { Abort("missing-repaint"); return false; }
            var windows = Find.WindowStack.Windows;
            int index = windows.IndexOf(_window);
            if (index < 0 || windows.Skip(index + 1).Any(w => w.windowRect.Overlaps(_window.windowRect)))
            { Abort("window-occluded"); return false; }
            if (Screen.width != _manifest.Width || Screen.height != _manifest.Height || Prefs.UIScale != _manifest.UiScale)
            { Abort("display-changed"); return false; }
            return true;
        }

        private byte[]? CaptureImage()
        {
            Texture2D? texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null || texture.width != _manifest.Width || texture.height != _manifest.Height)
                    throw new InvalidOperationException();
                return ImageConversion.EncodeToPNG(texture);
            }
            catch (Exception ex) { Abort("capture-failed-" + ex.GetType().Name); return null; }
            finally { if (texture != null) Destroy(texture); }
        }

        private bool ValidateImage(byte[] bytes)
        {
            var texture = new Texture2D(2, 2);
            try { return ImageConversion.LoadImage(texture, bytes) && texture.width == _manifest.Width && texture.height == _manifest.Height; }
            catch { return false; }
            finally { Destroy(texture); }
        }

        private void Abort(string reason) { _sequence.Fail(reason); Finish(); }

        private void CloseOwnedWindow()
        {
            if (_window?.IsOpen == true) _window.Close(doCloseSound: false);
            _window = null;
        }

        private void Finish()
        {
            if (_finishing || _manifest == null) return;
            _finishing = true;
            CloseOwnedWindow();
            _manifest.Status = _sequence.IsComplete ? "captured" : _sequence.IsCancelled ? "cancelled" : "failed";
            _manifest.Error = _sequence.Error;
            _manifestWrite = _artifacts.WriteManifestAsync(JsonConvert.SerializeObject(_manifest, Formatting.Indented));
        }

        private void OnDestroy()
        {
            if (_sequence != null && !_finishing) { _sequence.Cancel(); Finish(); }
            if (_active == this) _active = null;
        }

        private sealed class CaptureManifest
        {
            public string RunId = "", Status = "running", VisualReview = "not-reviewed", CoreAssemblyMvid = "", GameAssemblyMvid = "", GameVersion = "", Language = "";
            public string? Error;
            public int Width, Height;
            public float UiScale;
            public string[] ExpectedSceneIds = Array.Empty<string>();
            public List<CaptureSceneReceipt> Scenes = new();
        }

        private sealed class CaptureSceneReceipt
        {
            public string Id = "", PageId = "", File = "", Status = "pending";
            public string? Error = null;
            public int Frame, Width, Height;
            public string[] AdditionalFiles = Array.Empty<string>();
            public int[] AdditionalFrames = Array.Empty<int>();
        }
    }
}
