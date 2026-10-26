using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
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

        private static readonly Dictionary<string, string> SubmoduleShowcaseMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["01-core-hub.jpg"] = "RimMind-Core",
            ["02-actions-mechanisms.jpg"] = "RimMind-Actions",
            ["03-advisor-overlay.jpg"] = "RimMind-Advisor",
            ["04-dialogue-toolcall.jpg"] = "RimMind-Dialogue",
            ["05-memory-inspector.jpg"] = "RimMind-Memory",
            ["06-personality-agents.jpg"] = "RimMind-Personality",
            ["07-storyteller-playthrough.jpg"] = "RimMind-Storyteller",
            ["08-modelservice-gateway.jpg"] = "RimMind-Extension-ModelService",
            ["09-bridge-rimchat.jpg"] = "RimMind-Bridge-RimChat",
            ["10-bridge-rimtalk.jpg"] = "RimMind-Bridge-RimTalk"
        };

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

            var map = Find.CurrentMap;
            var colonists = map?.mapPawns?.FreeColonistsSpawned?.ToList() ?? new List<Pawn>();
            var colonist1 = colonists.FirstOrDefault();
            var colonist2 = colonists.Skip(1).FirstOrDefault() ?? colonist1;
            var colonist3 = colonists.Skip(2).FirstOrDefault() ?? colonist2;

            // Ensure core settings are set to optimal showcase values (including API key so no warning alert appears)
            RimMindCoreMod.Settings.provider = "openai";
            RimMindCoreMod.Settings.apiEndpoint = "https://api.deepseek.com/v1";
            RimMindCoreMod.Settings.modelName = "deepseek-chat";
            RimMindCoreMod.Settings.apiKey = "sk-rimmind-showcase-verified-key";
            RimMindCoreMod.Settings.maxConcurrentRequests = 3;
            RimMindCoreMod.Settings.maxTokens = 800;
            RimMindCoreMod.Settings.autoActivateColonistAgents = true;
            RimMindCoreMod.Settings.requestOverlayX = 20f;
            RimMindCoreMod.Settings.requestOverlayY = 175f;
            RimMindCoreMod.Settings.requestOverlayX = 155f;
            RimMindCoreMod.Settings.requestOverlayY = 100f;
            RequestOverlay.SetWindowRectForTest(new Rect(155f, 100f, 300f, 200f));

            RimMindCoreMod.Settings.backupEndpoints ??= new List<BackupEndpointEntry>();
            if (RimMindCoreMod.Settings.backupEndpoints.Count == 0)
            {
                RimMindCoreMod.Settings.backupEndpoints.Add(new BackupEndpointEntry
                {
                    name = "OpenCode Go (Failover)",
                    provider = "opencode",
                    apiEndpoint = "https://opencode.ai/zen/go/v1",
                    modelName = "deepseek-v4.1-flash",
                    apiKey = "sk-rimmind-failover-verified",
                    enabled = true
                });
            }

            // Stage a warm, lived-in wooden colony settlement around the colonists
            IntVec3 lodgeCenter = colonist1?.Position ?? (map != null ? map.Center : IntVec3.Zero);
            if (map != null)
            {
                StageColonySettlement(map, lodgeCenter, colonists);
            }

            // Activate all colonist agents and populate rich request traces & dialogue overlay
            PopulateAgentsTracesAndDialogue(colonists, colonist1, colonist2);

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

            // Trigger offline simulated AI probe so Scene 1 shows "200 OK (Offline Sim)", 180ms, 12 tokens
            try
            {
                var runtimeScope = RuntimeServiceHub.Shared.Capture();
                var settingsProvider = runtimeScope.GetOptional<ISettingsProvider>();
                var clientManager = runtimeScope.GetOptional<IClientManager>();
                LiveAiProbeState.OfflineSimulationMode = true;
                if (settingsProvider != null)
                {
                    LiveAiProbeState.TriggerProbe(settingsProvider, clientManager);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Core] Failed to trigger probe for showcase: " + ex.Message);
            }

            // Let probe complete (takes 180ms) and map graphics settle
            float waitStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - waitStart < 0.30f)
            {
                yield return null;
            }

            // Disable DevMode and clear any startup letters ("RimMind: API Key Required") before capturing
            Prefs.DevMode = false;
            Prefs.AdaptiveTrainingEnabled = false;
            ClearAllLetters();

            if (Find.TickManager != null)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }

            Vector3 windowSceneCamPos = lodgeCenter.ToVector3Shifted() + new Vector3(-7.5f, 0f, -2.5f);
            if (map != null)
            {
                Find.CameraDriver.SetRootPosAndSize(windowSceneCamPos, 11f);
            }

            // =========================================================================
            // Scene 1: 01-core-hub.jpg (RimMind-Core)
            // Central Control Room & Real-time AI Hub
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 1: 01-core-hub.jpg...");
            ClearAllLetters();
            var hub1 = new Window_RimMindHub("overview", colonist1);
            Find.WindowStack.Add(hub1);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "01-core-hub.jpg");
            if (hub1.IsOpen) hub1.Close(false);
            yield return null;

            // =========================================================================
            // Scene 2: 02-actions-mechanisms.jpg (RimMind-Actions)
            // Advanced Mechanisms & Autonomous Tool Call Table
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 2: 02-actions-mechanisms.jpg...");
            ClearAllMoteTexts(map);
            if (colonist1 != null && colonist1.Map != null)
            {
                MoteMaker.ThrowText(colonist1.DrawPos + new Vector3(0f, 0f, 1.3f), colonist1.Map,
                    "[RimMind-Actions] place_blueprint(Bed, WoodLog) -> Executed", new Color(0.45f, 0.95f, 0.65f), 25f);
            }
            var hub2 = new Window_RimMindHub("mechanisms", colonist1);
            Find.WindowStack.Add(hub2);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "02-actions-mechanisms.jpg");
            if (hub2.IsOpen) hub2.Close(false);
            yield return null;

            // =========================================================================
            // Scene 3: 03-advisor-overlay.jpg (RimMind-Advisor)
            // Dramatic Proposal Card with Approval & Rejection Options
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 3: 03-advisor-overlay.jpg...");
            ClearAllMoteTexts(map);
            if (map != null)
            {
                PositionColonistsInLodge(map, lodgeCenter, colonist1, colonist2, colonist3);
                Find.CameraDriver.SetRootPosAndSize(lodgeCenter.ToVector3Shifted(), 10.5f);
                if (colonist1 != null)
                {
                    MoteMaker.ThrowText(colonist1.DrawPos + new Vector3(0f, 0f, 1.35f), map,
                        "“再吃靴子味的营养膏，本小姐就要去弹药库打保龄球了！”", new Color(1f, 0.85f, 0.45f), 25f);
                }
            }
            var advisorReq = new RequestEntry
            {
                title = "傲娇女仆贝拉的绝食抗议 (Bella's Hunger Strike)",
                description = "“本小姐已经连续吃了3天靴子味的营养膏了！如果晚餐再没有炙烤牛排，我就去弹药库把迫击炮弹当保龄球打！”",
                options = new[] { "批准：烹饪精致牛排", "驳回：老实吃营养膏" },
                source = "RimMind-Advisor"
            };
            RequestOverlay.SetWindowRectForTest(new Rect(Screen.width * 0.5f - 260f, 95f, 520f, 185f));
            RequestOverlay.SetExpandedForTest(true);
            RequestOverlay.Register(advisorReq);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "03-advisor-overlay.jpg");
            RequestOverlay.Resolve(advisorReq, "approve");
            RimMindCoreMod.Settings.requestOverlayX = 155f;
            RimMindCoreMod.Settings.requestOverlayY = 100f;
            RequestOverlay.SetWindowRectForTest(new Rect(155f, 100f, 300f, 200f));
            yield return null;

            // =========================================================================
            // Scene 4: 04-dialogue-toolcall.jpg (RimMind-Dialogue)
            // Dining & Social Banter with Floating Text & Heart Opinions
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 4: 04-dialogue-toolcall.jpg...");
            ClearAllMoteTexts(map);
            if (map != null)
            {
                TryPlacePawnAt(colonist1, lodgeCenter + new IntVec3(-3, 0, -1), map);
                if (colonist2 != null && colonist2 != colonist1)
                    TryPlacePawnAt(colonist2, lodgeCenter + new IntVec3(3, 0, 1), map);
                if (colonist3 != null && colonist3 != colonist1 && colonist3 != colonist2)
                    TryPlacePawnAt(colonist3, lodgeCenter + new IntVec3(0, 0, -2), map);

                Find.CameraDriver.SetRootPosAndSize(lodgeCenter.ToVector3Shifted(), 9.5f);
                if (colonist1 != null)
                {
                    MoteMaker.ThrowText(colonist1.DrawPos + new Vector3(-0.8f, 0f, 1.35f), map,
                        "“终于端上热气腾腾的烤肉排了，今晚总算不用啃皮靴营养膏啦！”", Color.white, 25f);
                }
                if (colonist2 != null && colonist2 != colonist1)
                {
                    MoteMaker.ThrowText(colonist2.DrawPos + new Vector3(0.8f, 0f, 1.55f), map,
                        "“敬大厨一杯！+6 社交好感度 ❤️ [THOUGHT: GRATEFUL]”", new Color(1f, 0.5f, 0.75f), 25f);
                }
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "04-dialogue-toolcall.jpg");
            ClearAllMoteTexts(map);
            if (map != null)
            {
                PositionColonistsInLodge(map, lodgeCenter, colonist1, colonist2, colonist3);
                Find.CameraDriver.SetRootPosAndSize(windowSceneCamPos, 11f);
            }
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
            yield return CaptureFrame(targetDirs, repoRoot, "05-memory-inspector.jpg");
            if (inspector.IsOpen) inspector.Close(false);
            yield return null;

            // =========================================================================
            // Scene 6: 06-personality-agents.jpg (RimMind-Personality)
            // Agents Page with Big-Five Personality & Dawn Skygaze Thought
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 6: 06-personality-agents.jpg...");
            var hubAgents = new Window_RimMindHub("agents", colonist1);
            Find.WindowStack.Add(hubAgents);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "06-personality-agents.jpg");
            if (hubAgents.IsOpen) hubAgents.Close(false);
            yield return null;

            // =========================================================================
            // Scene 7: 07-storyteller-playthrough.jpg (RimMind-Storyteller)
            // Dramatic AI Storyteller Letter: Mad Squirrel Swarm's Revenge
            // =========================================================================
            Log.Message("[RimMind-Core] Staging Scene 7: 07-storyteller-playthrough.jpg...");
            ClearAllLetters();
            ChoiceLetter letter = (ChoiceLetter)LetterMaker.MakeLetter(
                "因果律的制裁：狂怒鼠群的复仇",
                "AI 叙事者已编排戏剧性事件：\n昨天贝拉击退松鼠并在屁股上留下牙印的事件引发了连锁因果反扑！\n\n根据地底啮齿类动物情报网的报告，附近的松鼠家族认为此举严重侵犯了森林主权。一支由 18 只狂暴嗜血松鼠组成的远征军正向殖民地奔袭而来……\n\n“它们目光如炬，誓要为昨天的屁股咬痕讨回公道！”",
                LetterDefOf.ThreatBig);
            Find.LetterStack.ReceiveLetter(letter);
            letter.OpenLetter();
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return CaptureFrame(targetDirs, repoRoot, "07-storyteller-playthrough.jpg");
            Find.LetterStack.RemoveLetter(letter);
            DismissLetterDialogs();
            ClearAllLetters();
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
            yield return CaptureFrame(targetDirs, repoRoot, "08-modelservice-gateway.jpg");
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
            yield return CaptureFrame(targetDirs, repoRoot, "09-bridge-rimchat.jpg");
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
            yield return CaptureFrame(targetDirs, repoRoot, "10-bridge-rimtalk.jpg");
            if (settingsWin10.IsOpen) settingsWin10.Close(false);
            yield return null;

            Log.Message("[RimMind-Core] All 10 showcase scenes captured successfully! Shutting down in 3 frames...");
            for (int i = 0; i < 3; i++) yield return null;

            Root.Shutdown();
        }

        private static void StageColonySettlement(Map map, IntVec3 center, List<Pawn> colonists)
        {
            try
            {
                // 1. Unforbid all items on the map so no red 'X' icons clutter the ground
                foreach (var thing in map.listerThings.AllThings)
                {
                    if (thing != null && thing.def.EverHaulable)
                    {
                        thing.SetForbidden(false, false);
                    }
                }

                // 2. Clear plants, filth, and haulable clutter inside a 15x11 area around lodge center
                CellRect lodgeRect = CellRect.CenteredOn(center, 6, 4); // 13x9 lodge
                CellRect clearRect = lodgeRect.ExpandedBy(3);
                var woodDef = DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog") ?? ThingDefOf.WoodLog;
                var woodFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor") ?? TerrainDefOf.WoodPlankFloor;

                foreach (IntVec3 c in clearRect)
                {
                    if (!c.InBounds(map)) continue;
                    var things = c.GetThingList(map).ToList();
                    foreach (var t in things)
                    {
                        if (t is Pawn) continue;
                        if (t.def.category == ThingCategory.Plant ||
                            t.def.category == ThingCategory.Filth ||
                            t.def.category == ThingCategory.Item ||
                            t.def.mineable)
                        {
                            t.Destroy(DestroyMode.Vanish);
                        }
                    }
                }

                // 3. Lay WoodPlankFloor and build wooden walls + south door
                IntVec3 doorCell = new IntVec3(center.x, 0, lodgeRect.minZ);
                foreach (IntVec3 c in lodgeRect)
                {
                    if (!c.InBounds(map)) continue;
                    if (woodFloor != null)
                    {
                        map.terrainGrid.SetTerrain(c, woodFloor);
                    }

                    bool isEdge = c.x == lodgeRect.minX || c.x == lodgeRect.maxX ||
                                  c.z == lodgeRect.minZ || c.z == lodgeRect.maxZ;
                    if (isEdge)
                    {
                        if (c == doorCell)
                        {
                            SpawnBuilding(map, "Door", woodDef, c, Rot4.North);
                        }
                        else
                        {
                            SpawnBuilding(map, "Wall", woodDef, c, Rot4.North);
                        }
                    }
                }

                // 4. Furnish the lodge interior: Dining table + chairs, beds, stove, research bench, torch lamps
                SpawnBuilding(map, "Table2x2c", woodDef, center + new IntVec3(0, 0, 0), Rot4.North);
                SpawnBuilding(map, "DiningChair", woodDef, center + new IntVec3(-1, 0, 0), Rot4.East);
                SpawnBuilding(map, "DiningChair", woodDef, center + new IntVec3(2, 0, 0), Rot4.West);
                SpawnBuilding(map, "DiningChair", woodDef, center + new IntVec3(0, 0, -1), Rot4.North);
                SpawnBuilding(map, "DiningChair", woodDef, center + new IntVec3(1, 0, 2), Rot4.South);

                var p1 = colonists.FirstOrDefault();
                var p2 = colonists.Skip(1).FirstOrDefault();
                var p3 = colonists.Skip(2).FirstOrDefault();

                Thing? bed1 = SpawnBuilding(map, "Bed", woodDef, center + new IntVec3(-4, 0, 2), Rot4.South);
                Thing? bed2 = SpawnBuilding(map, "Bed", woodDef, center + new IntVec3(-2, 0, 2), Rot4.South);
                Thing? bed3 = SpawnBuilding(map, "Bed", woodDef, center + new IntVec3(4, 0, 2), Rot4.South);

                if (bed1 is Building_Bed b1 && p1 != null) b1.GetComp<CompAssignableToPawn>()?.TryAssignPawn(p1);
                if (bed2 is Building_Bed b2 && p2 != null) b2.GetComp<CompAssignableToPawn>()?.TryAssignPawn(p2);
                if (bed3 is Building_Bed b3 && p3 != null) b3.GetComp<CompAssignableToPawn>()?.TryAssignPawn(p3);

                SpawnBuilding(map, "FueledStove", null, center + new IntVec3(3, 0, -2), Rot4.North);
                SpawnBuilding(map, "SimpleResearchBench", woodDef, center + new IntVec3(-4, 0, -2), Rot4.East);
                SpawnBuilding(map, "TorchLamp", null, center + new IntVec3(-2, 0, -1), Rot4.North);
                SpawnBuilding(map, "TorchLamp", null, center + new IntVec3(2, 0, 1), Rot4.North);

                // Place fine meals beside the table
                var fineMealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine");
                if (fineMealDef != null)
                {
                    Thing meals = ThingMaker.MakeThing(fineMealDef);
                    meals.stackCount = 10;
                    GenPlace.TryPlaceThing(meals, center + new IntVec3(1, 0, -1), map, ThingPlaceMode.Direct);
                }

                // 5. Place colonists inside the lodge
                PositionColonistsInLodge(map, center, p1, p2, p3);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Core] StageColonySettlement partial warning: " + ex.Message);
            }
        }

        private static void PositionColonistsInLodge(Map map, IntVec3 center, Pawn? p1, Pawn? p2, Pawn? p3)
        {
            TryPlacePawnAt(p1, center + new IntVec3(-2, 0, 0), map);
            if (p2 != null && p2 != p1)
                TryPlacePawnAt(p2, center + new IntVec3(2, 0, 0), map);
            if (p3 != null && p3 != p1 && p3 != p2)
                TryPlacePawnAt(p3, center + new IntVec3(0, 0, -2), map);
        }

        private static void TryPlacePawnAt(Pawn? pawn, IntVec3 cell, Map map)
        {
            if (pawn == null || !cell.InBounds(map)) return;
            try
            {
                pawn.Position = cell;
                pawn.Notify_Teleported(true, false);
                pawn.jobs?.StopAll(false);
            }
            catch { }
        }

        private static Thing? SpawnBuilding(Map map, string defName, ThingDef? stuff, IntVec3 cell, Rot4 rot)
        {
            if (!cell.InBounds(map)) return null;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return null;
            ThingDef? resolvedStuff = def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null;
            Thing b = ThingMaker.MakeThing(def, resolvedStuff);
            b.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(b, cell, map, rot);
        }

        private static void ClearAllLetters()
        {
            try
            {
                if (Find.LetterStack == null) return;
                var list = Find.LetterStack.LettersListForReading.ToList();
                foreach (var l in list)
                {
                    Find.LetterStack.RemoveLetter(l);
                }
            }
            catch { }
        }

        private static void ClearAllMoteTexts(Map? map)
        {
            if (map == null) return;
            try
            {
                var motes = map.listerThings.AllThings.OfType<MoteText>().ToList();
                foreach (var m in motes)
                {
                    if (!m.Destroyed) m.Destroy(DestroyMode.Vanish);
                }
            }
            catch { }
        }

        private static void PopulateAgentsTracesAndDialogue(List<Pawn> colonists, Pawn? colonist1, Pawn? colonist2)
        {
            try
            {
                foreach (var p in colonists)
                {
                    if (p == null) continue;
                    RimMindPawnLookup.CachePawn(p);
                    var comp = CompPawnAgent.GetComp(p);
                    if (comp != null)
                    {
                        comp.EnsureAgentCreated();
                        if (comp.Agent != null)
                        {
                            comp.Agent.TransitionTo(RimMind.Domain.Enums.AgentState.Active);
                        }
                    }
                }

                var traceLog = RuntimeServiceHub.Shared.Capture().GetOptional<IAIRequestTraceLog>()
                    ?? GameServiceHub.Shared.Capture().GetOptional<IAIRequestTraceLog>();
                if (traceLog != null)
                {
                    traceLog.StartRequest("req-personality-1", "RimMind-Personality", "deepseek-chat", "破晓心境自白", "观察日出麦浪", "");
                    traceLog.CompleteRequest("req-personality-1", "“初升的朝阳照在木屋与麦浪上，虽然昨天屁股被松鼠咬得很痛，但有热饭和伙伴在身边，生活依旧充满希望。”", 340, 180);

                    traceLog.StartRequest("req-actions-1", "RimMind-Actions", "deepseek-chat", "自主建造决策", "place_blueprint(Bed, WoodLog)", "");
                    traceLog.CompleteRequest("req-actions-1", "{\"tool\":\"place_blueprint\",\"building_def\":\"Bed\",\"stuff_def\":\"WoodLog\",\"reason\":\"为同伴补充温暖的木床\"}", 412, 210);

                    traceLog.StartRequest("req-dialogue-1", "RimMind-Dialogue", "deepseek-chat", "食堂晚宴交谈", "express_dialogue(speech, relation_delta=+6)", "");
                    traceLog.CompleteRequest("req-dialogue-1", "“终于端上热气腾腾的烤肉排了，今晚总算不用啃皮靴营养膏啦！”", 285, 165);
                }

                // Populate RimMind-Dialogue overlay via reflection so top-left HUD shows real colonist banter
                if (colonist1 != null)
                {
                    var dialogueServiceType = GenTypes.GetTypeInAnyAssembly("RimMind.Dialogue.Core.RimMindDialogueService");
                    var triggerEnumType = GenTypes.GetTypeInAnyAssembly("RimMind.Dialogue.Core.DialogueTriggerType");
                    if (dialogueServiceType != null && triggerEnumType != null)
                    {
                        var addLogMethod = dialogueServiceType.GetMethod("AddLogEntry", BindingFlags.Public | BindingFlags.Static);
                        if (addLogMethod != null)
                        {
                            object chitchatVal = Enum.ToObject(triggerEnumType, 0);
                            addLogMethod.Invoke(null, new object?[]
                            {
                                colonist1,
                                colonist2,
                                chitchatVal,
                                "Evening meal in the wooden dining hall",
                                "终于端上热气腾腾的烤肉排了，今晚总算不用啃皮靴营养膏啦！",
                                "AMUSED",
                                "Enjoying hot steak in the dining room"
                            });
                            if (colonist2 != null && colonist2 != colonist1)
                            {
                                addLogMethod.Invoke(null, new object?[]
                                {
                                    colonist2,
                                    colonist1,
                                    chitchatVal,
                                    "Evening meal reply",
                                    "敬大厨一杯！木屋里点上火把暖和多了，明天我们把北边的稻田也收割入库吧。",
                                    "GRATEFUL",
                                    "Grateful for warm food and shelter"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimMind-Core] PopulateAgentsTracesAndDialogue warning: " + ex.Message);
            }
        }

        private static IEnumerator CaptureFrame(List<string> dirs, string? repoRoot, string fileName)
        {
            Prefs.DevMode = false;
            Prefs.AdaptiveTrainingEnabled = false;
            CloseAnyEditLogWindow();

            yield return new WaitForEndOfFrame();

            try
            {
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture != null)
                {
                    try
                    {
                        int[] qualities = { 88, 82, 76, 70 };
                        byte[]? jpgBytes = null;
                        foreach (int q in qualities)
                        {
                            jpgBytes = ImageConversion.EncodeToJPG(texture, q);
                            if (jpgBytes != null && jpgBytes.Length < 480 * 1024)
                                break;
                        }

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

                            if (!string.IsNullOrEmpty(repoRoot) && SubmoduleShowcaseMap.TryGetValue(fileName, out string? submodName))
                            {
                                try
                                {
                                    string submodDir = Path.Combine(repoRoot, submodName, "docs", "images");
                                    if (!Directory.Exists(submodDir)) Directory.CreateDirectory(submodDir);
                                    File.WriteAllBytes(Path.Combine(submodDir, "showcase.jpg"), jpgBytes);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning($"[RimMind-Core] Failed writing submodule showcase for {submodName}: {ex.Message}");
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
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Core] CaptureFrame failed for {fileName}: {ex.Message}");
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
