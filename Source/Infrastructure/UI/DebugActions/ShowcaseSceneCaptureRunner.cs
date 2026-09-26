using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI;
using RimMind.Presentation.UI.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugActions
{
    /// <summary>
    /// Automated in-game staging, dramatic scene capture, and JPEG 90 compression runner
    /// for all 10 RimMind modules. Produces clean, rich, and fun showcase images.
    /// </summary>
    internal sealed class ShowcaseSceneCaptureRunner : MonoBehaviour
    {
        private static ShowcaseSceneCaptureRunner? _active;
        private static bool _startupChecked;

        internal static void CheckStartup()
        {
            if (_startupChecked || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            _startupChecked = true;

            if (GenCommandLine.TryGetCommandLineArg("rimmind-showcase-capture", out string runId))
            {
                GenCommandLine.TryGetCommandLineArg("rimmind-repo-root", out string repoRoot);
                StartCapture(runId, repoRoot);
            }
        }

        internal static void StartCapture(string runId, string? repoRoot = null)
        {
            if (_active != null)
            {
                Log.Warning("[RimMind-Core] Showcase scene capture already running.");
                return;
            }

            try
            {
                var runner = Current.Root.gameObject.AddComponent<ShowcaseSceneCaptureRunner>();
                _active = runner;
                runner.StartCoroutine(runner.Run(runId, repoRoot));
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Core] Showcase scene capture failed to start: " + ex);
                _active = null;
            }
        }

        private IEnumerator Run(string runId, string? repoRoot)
        {
            Log.Message($"[RimMind-Core] Starting ShowcaseSceneCaptureRunner for RunId: {runId}...");

            string sandboxDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "ShowcaseCaptures", runId);
            var targetDirs = new List<string> { sandboxDir };

            if (!string.IsNullOrEmpty(repoRoot))
            {
                string docsDir = Path.Combine(repoRoot, "docs", "images", "showcase");
                targetDirs.Add(docsDir);
            }

            foreach (var dir in targetDirs)
            {
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
            }

            // Let the world tick and settle for a few frames
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            // Close any startup log windows
            CloseAnyEditLogWindow();

            var colonist1 = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
            var colonist2 = Find.CurrentMap?.mapPawns?.FreeColonists?.Skip(1).FirstOrDefault() ?? colonist1;

            // Ensure core settings are set to optimal showcase values
            RimMindCoreMod.Settings.provider = "openai";
            RimMindCoreMod.Settings.apiEndpoint = "https://api.deepseek.com/v1";
            RimMindCoreMod.Settings.modelName = "deepseek-chat";
            RimMindCoreMod.Settings.maxConcurrentRequests = 2;
            RimMindCoreMod.Settings.maxTokens = 800;

            // Ensure mechanisms are registered in registry
            try
            {
                var registry = GameServiceHub.Shared.Capture().GetOptional<IGameMechanismRegistry>()
                    ?? RimMindAPI.Mechanisms;
                if (registry != null && registry.All.Count == 0)
                {
                    ToolMechanismComposition.RegisterAllMechanisms(registry);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Core] Failed to populate mechanisms for showcase: " + ex.Message);
            }

            // =========================================================================
            // Scene 1: 01-core-hub.jpg (RimMind-Core)
            // Central Control Room & Real-time AI Hub
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 1: 01-core-hub.jpg...");
            var hub1 = new Window_RimMindHub("overview", null);
            Find.WindowStack.Add(hub1);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "01-core-hub.jpg");
            if (hub1.IsOpen) hub1.Close(false);
            yield return null;

            // =========================================================================
            // Scene 2: 02-actions-mechanisms.jpg (RimMind-Actions)
            // Advanced Mechanisms & Autonomous Tool Call Table
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 2: 02-actions-mechanisms.jpg...");
            var hub2 = new Window_RimMindHub("mechanisms", null);
            Find.WindowStack.Add(hub2);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "02-actions-mechanisms.jpg");
            if (hub2.IsOpen) hub2.Close(false);
            yield return null;

            // =========================================================================
            // Scene 3: 03-advisor-overlay.jpg (RimMind-Advisor)
            // Dramatic Proposal Card with Approval & Rejection Options
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 3: 03-advisor-overlay.jpg...");
            if (colonist1 != null && colonist1.Map != null)
            {
                Find.CameraDriver.SetRootPosAndSize(colonist1.Position.ToVector3Shifted(), 12f);
            }
            var advisorReq = new RequestEntry
            {
                title = "傲娇女仆贝拉的绝食抗议 (Bella's Hunger Strike)",
                description = "“本小姐已经连续吃了3天靴子味的营养膏了！如果晚餐再没有炙烤牛排，我就去弹药库把迫击炮弹当保龄球打！”",
                options = new[] { "批准：烹饪精致牛排", "驳回：老实吃营养膏" },
                source = "RimMind-Advisor"
            };
            RequestOverlay.SetWindowRectForTest(new Rect(Screen.width * 0.5f - 240f, 90f, 480f, 180f));
            RequestOverlay.Register(advisorReq);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "03-advisor-overlay.jpg");
            RequestOverlay.Resolve(advisorReq, "approve");
            yield return null;

            // =========================================================================
            // Scene 4: 04-dialogue-toolcall.jpg (RimMind-Dialogue)
            // Dining & Social Banter with Floating Text & Heart Opinions
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 4: 04-dialogue-toolcall.jpg...");
            if (colonist1 != null && colonist1.Map != null)
            {
                Find.CameraDriver.SetRootPosAndSize(colonist1.Position.ToVector3Shifted(), 10f);
                MoteMaker.ThrowText(colonist1.DrawPos + new Vector3(0, 0, 1.4f), colonist1.Map, "“这营养膏尝起来简直像把陈年皮靴扔进搅拌机打成糊……”", Color.white, 25f);
                if (colonist2 != null && colonist2.Map != null)
                {
                    MoteMaker.ThrowText(colonist2.DrawPos + new Vector3(0, 0, 1.4f), colonist2.Map, "“同感！+6 社交好感度 ❤️”", new Color(1f, 0.45f, 0.7f), 25f);
                }
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "04-dialogue-toolcall.jpg");
            yield return null;

            // =========================================================================
            // Scene 5: 05-memory-inspector.jpg (RimMind-Memory)
            // Context Payload Inspector with 4-Zone Cache Breakdown
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 5: 05-memory-inspector.jpg...");
            var inspector = new Window_ContextPayloadInspector(colonist1);
            inspector.SetViewForTest(colonist1, 3, 3); // Scenario 3 (Memory), Tab 3 (Analysis)
            Find.WindowStack.Add(inspector);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "05-memory-inspector.jpg");
            if (inspector.IsOpen) inspector.Close(false);
            yield return null;

            // =========================================================================
            // Scene 6: 06-personality-agents.jpg (RimMind-Personality)
            // Agents Page with Big-Five Personality & Dawn Skygaze Thought
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 6: 06-personality-agents.jpg...");
            if (colonist1 != null)
            {
                var comp = CompPawnAgent.GetComp(colonist1);
                if (comp != null)
                {
                    comp.EnsureAgentCreated();
                    if (comp.Agent != null)
                    {
                        comp.Agent.TransitionTo(RimMind.Domain.Enums.AgentState.Active);
                    }
                }
                var traceLog = RuntimeServiceHub.Shared.Capture().GetOptional<IAIRequestTraceLog>()
                    ?? GameServiceHub.Shared.Capture().GetOptional<IAIRequestTraceLog>();
                if (traceLog != null)
                {
                    traceLog.StartRequest("req-personality-1", "RimMind-Personality", "deepseek-chat", "破晓心境自白", "观察日出麦浪", "");
                    traceLog.CompleteRequest("req-personality-1", "“初升的朝阳照在麦浪上，虽然昨天屁股被松鼠咬得很痛，但生活依旧充满希望。”", 340, 180);
                }
            }
            var hubAgents = new Window_RimMindHub("agents", colonist1);
            Find.WindowStack.Add(hubAgents);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "06-personality-agents.jpg");
            if (hubAgents.IsOpen) hubAgents.Close(false);
            yield return null;

            // =========================================================================
            // Scene 7: 07-storyteller-playthrough.jpg (RimMind-Storyteller)
            // Dramatic AI Storyteller Letter: Mad Squirrel Swarm's Revenge
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 7: 07-storyteller-playthrough.jpg...");
            ChoiceLetter letter = (ChoiceLetter)LetterMaker.MakeLetter(
                "因果律的制裁：狂怒鼠群的复仇",
                "AI 叙事者已编排戏剧性事件：\n昨天贝拉击退松鼠并在屁股上留下牙印的事件引发了连锁因果反扑！\n\n根据地底啮齿类动物情报网的报告，附近的松鼠家族认为此举严重侵犯了森林主权。一支由 18 只狂暴嗜血松鼠组成的远征军正向殖民地奔袭而来……\n\n“它们目光如炬，誓要为昨天的屁股咬痕讨回公道！”",
                LetterDefOf.ThreatBig);
            Find.LetterStack.ReceiveLetter(letter);
            letter.OpenLetter();
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "07-storyteller-playthrough.jpg");
            Find.LetterStack.RemoveLetter(letter);
            DismissLetterDialogs();
            yield return null;

            // =========================================================================
            // Scene 8: 08-modelservice-gateway.jpg (RimMind-Extension-ModelService)
            // Model Service Gateway & Failover Settings
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 8: 08-modelservice-gateway.jpg...");
            var settingsWin8 = new Window_RimMindSettings();
            Find.WindowStack.Add(settingsWin8);
            RimMindCoreSettingsUI.CurrentTab = "model_service";
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "08-modelservice-gateway.jpg");
            if (settingsWin8.IsOpen) settingsWin8.Close(false);
            yield return null;

            // =========================================================================
            // Scene 9: 09-bridge-rimchat.jpg (RimMind-Bridge-RimChat)
            // RimChat Bridge Mutual Exclusion & Diplomacy Gating
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 9: 09-bridge-rimchat.jpg...");
            var settingsWin9 = new Window_RimMindSettings();
            Find.WindowStack.Add(settingsWin9);
            RimMindCoreSettingsUI.CurrentTab = "bridge_rimchat";
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "09-bridge-rimchat.jpg");
            if (settingsWin9.IsOpen) settingsWin9.Close(false);
            yield return null;

            // =========================================================================
            // Scene 10: 10-bridge-rimtalk.jpg (RimMind-Bridge-RimTalk)
            // RimTalk Bridge Comic Bubbles & Memory Synchronization
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 10: 10-bridge-rimtalk.jpg...");
            var settingsWin10 = new Window_RimMindSettings();
            Find.WindowStack.Add(settingsWin10);
            RimMindCoreSettingsUI.CurrentTab = "bridge_rimtalk";
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, "10-bridge-rimtalk.jpg");
            if (settingsWin10.IsOpen) settingsWin10.Close(false);
            yield return null;

            Log.Message("[RimMind-Core] All 10 showcase scenes captured successfully! Shutting down in 3 frames...");
            for (int i = 0; i < 3; i++) yield return null;

            Root.Shutdown();
        }

        private static IEnumerator CaptureFrame(List<string> dirs, string fileName)
        {
            yield return new WaitForEndOfFrame();

            bool origDev = Prefs.DevMode;
            bool origAdaptive = Prefs.AdaptiveTrainingEnabled;

            try
            {
                Prefs.DevMode = false;
                Prefs.AdaptiveTrainingEnabled = false;

                CloseAnyEditLogWindow();

                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture != null)
                {
                    try
                    {
                        byte[] jpgBytes = ImageConversion.EncodeToJPG(texture, 90);
                        if (jpgBytes != null && jpgBytes.Length > 0)
                        {
                            foreach (var dir in dirs)
                            {
                                try
                                {
                                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                    string fullPath = Path.Combine(dir, fileName);
                                    File.WriteAllBytes(fullPath, jpgBytes);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning($"[RimMind-Core] Failed writing to {dir}/{fileName}: {ex.Message}");
                                }
                            }
                            Log.Message($"[RimMind-Core] Captured showcase frame: {fileName} ({jpgBytes.Length / 1024} KB)");
                        }
                    }
                    finally
                    {
                        Destroy(texture);
                    }
                }
            }
            finally
            {
                Prefs.DevMode = origDev;
                Prefs.AdaptiveTrainingEnabled = origAdaptive;
            }
        }

        private static void CloseAnyEditLogWindow()
        {
            var logWin = Find.WindowStack.Windows.FirstOrDefault(w => w.GetType().Name == "EditWindow_Log");
            if (logWin != null)
            {
                logWin.Close(false);
            }
        }

        private static void DismissLetterDialogs()
        {
            var diag = Find.WindowStack.Windows.FirstOrDefault(w =>
                w.GetType().Name.Contains("Letter") ||
                w.GetType().Name.Contains("Dialog_NodeTree") ||
                w.GetType().Name.Contains("Dialog_MessageBox"));
            if (diag != null)
            {
                Find.WindowStack.TryRemove(diag, false);
            }
        }
    }
}
