using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using VerseMap = Verse.Map;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Flywheel;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Features.Json;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public sealed class BehaviorAutotestResult
    {
        public string SuiteId { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public long DurationMs { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; set; } = new();
    }

    public sealed class BehaviorAutotestReport
    {
        public string RunId { get; set; } = string.Empty;
        public string OverallStatus { get; set; } = "RUNNING";
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public int TotalPassed { get; set; }
        public int TotalFailed { get; set; }
        public long TotalDurationMs { get; set; }
        public List<BehaviorAutotestResult> Suites { get; set; } = new();
    }

    public sealed class ToolExecutionTestItem
    {
        public string ToolId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool SchemaValid { get; set; }
        public string InputPayload { get; set; } = string.Empty;
        public string Status { get; set; } = "UNKNOWN"; // PASS, DOMAIN_REJECT, FAIL
        public bool IsError { get; set; }
        public string ReturnSnippet { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public long DurationMs { get; set; }
        public bool BoundaryHandledGracefully { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class ToolsExecutionReport
    {
        public string RunId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int TotalTools { get; set; }
        public int Passed { get; set; }
        public int DomainRejected { get; set; }
        public int Failed { get; set; }
        public long TotalDurationMs { get; set; }
        public List<ToolExecutionTestItem> Items { get; set; } = new();
    }

    internal sealed class BehaviorAutotestRunner : MonoBehaviour
    {
        private static BehaviorAutotestRunner? _active;
        private static bool _startupChecked;

        private string _runId = string.Empty;
        private bool _isHeadless;
        private BehaviorAutotestReport _report = new();
        private readonly Stopwatch _clock = new();

        internal static void CheckStartup()
        {
            if (_startupChecked || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            _startupChecked = true;

            if (GenCommandLine.TryGetCommandLineArg("rimmind-behavior-test", out string runId))
            {
                StartSuite(runId, isHeadless: true);
            }
        }

        internal static void StartSuite(string? runId = null, bool isHeadless = false)
        {
            if (_active != null)
            {
                Log.Warning("[RimMind-Core] Behavior autotest suite is already running.");
                return;
            }

            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                Log.Warning("[RimMind-Core] Behavior autotest requires a loaded map.");
                return;
            }

            try
            {
                var runner = Current.Root.gameObject.AddComponent<BehaviorAutotestRunner>();
                _active = runner;
                runner.Initialize(runId ?? Guid.NewGuid().ToString("N"), isHeadless);
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Core] Failed to initialize behavior autotest runner: " + ex);
                _active = null;
            }
        }

        private void Initialize(string runId, bool isHeadless)
        {
            _runId = runId;
            _isHeadless = isHeadless;
            _clock.Start();

            // Override credentials from environment if provided (secure sandbox execution)
            string? envKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
            string? envEndpoint = Environment.GetEnvironmentVariable("RIMMIND_TEST_ENDPOINT");
            string? envModel = Environment.GetEnvironmentVariable("RIMMIND_TEST_MODEL");

            if (!string.IsNullOrWhiteSpace(envKey))
                RimMindCoreMod.Settings.apiKey = envKey;
            if (!string.IsNullOrWhiteSpace(envEndpoint))
                RimMindCoreMod.Settings.apiEndpoint = envEndpoint;
            if (!string.IsNullOrWhiteSpace(envModel))
                RimMindCoreMod.Settings.modelName = envModel;

            _report = new BehaviorAutotestReport
            {
                RunId = _runId,
                Provider = RimMindCoreMod.Settings.provider,
                Model = RimMindCoreMod.Settings.modelName,
                Endpoint = RimMindCoreMod.Settings.apiEndpoint,
            };

            Log.Message($"[RimMind-Core] Starting behavior autotest suite (runId={_runId}, headless={_isHeadless})");
            StartCoroutine(RunSuitesRoutine());
        }

        private IEnumerator RunSuitesRoutine()
        {
            // Yield a few frames for game map and pawn components to settle
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            // Suite 1: Architecture & Registry Contracts
            yield return RunSuiteArchitecture();

            // Suite 2: Live LLM End-to-End Connectivity & JSON Parsing
            yield return RunSuiteLiveLlm();

            // Suite 3: Context Snapshot & Budget Orchestration
            yield return RunSuiteContextSnapshot();

            // Suite 4: Pawn Agent Autonomy & Approval Queue
            yield return RunSuiteAutonomyQueue();

            // Suite 5: Flywheel & Telemetry
            yield return RunSuiteFlywheel();

            // Suite 6: Full In-Game Tools Execution
            yield return RunSuiteAllToolsExecution();

            // Suite 7+: Discovered Submodule Behavior Suites
            yield return RunDiscoveredModSuites();

            // Finalize and report
            FinalizeReport();
        }

        private IEnumerator RunSuiteArchitecture()
        {
            var sw = Stopwatch.StartNew();
            var result = new BehaviorAutotestResult { SuiteId = "Architecture.Contracts" };

            try
            {
                RimMindCoreDebugActions.TestH2ActionsEquivalence();
                result.Details.Add("TestH2ActionsEquivalence executed");

                RimMindCoreDebugActions.TestPVisibilityEntrypoints();
                result.Details.Add("TestPVisibilityEntrypoints executed");

                RimMindCoreDebugActions.TestKUnifiedRequest();
                result.Details.Add("TestKUnifiedRequest executed");

                RimMindCoreDebugActions.TestLContextEvolution();
                result.Details.Add("TestLContextEvolution executed");

                RimMindCoreDebugActions.TestUiLayoutConflictDetector();
                result.Details.Add("TestUiLayoutConflictDetector executed");

                result.PassCount = 5;
                result.Status = "PASS";
                result.Message = "All 5 core architecture and contract tests executed successfully.";
            }
            catch (Exception ex)
            {
                result.FailCount = 1;
                result.Status = "FAIL";
                result.Message = $"Architecture test failed: {ex.Message}";
                result.Details.Add(ex.ToString());
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            RecordSuiteResult(result);
            yield return null;
        }

        private IEnumerator RunSuiteLiveLlm()
        {
            var sw = Stopwatch.StartNew();
            var result = new BehaviorAutotestResult { SuiteId = "LiveLlm.EndToEnd" };

            if (!RimMindCoreMod.Settings.IsConfigured())
            {
                result.Status = "SKIP";
                result.Message = "API settings not configured. Skipped live LLM call.";
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                RecordSuiteResult(result);
                yield break;
            }

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("BehaviorTestConnection")
                .WithModId("Autotest")
                .WithGameStateInfo(new GameStateInfo().AddSection("perception", "autotest"))
                .WithMaxTokens(150)
                .WithTemperature(0.1f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            envelope.Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = "You are a test assistant for RimMind. Always respond strictly in JSON format."
            });
            envelope.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = "Reply with exact JSON: {\"status\":\"ok\",\"service\":\"rimmind\",\"echo\":\"verified\"}"
            });

            bool completed = false;
            Result<LlmResponse, RimMindError>? responseResult = null;

            RimMindAPI.Send(envelope, res =>
            {
                responseResult = res;
                completed = true;
            });

            float timeout = 40f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!completed)
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = $"Live LLM request timed out after {timeout} seconds.";
            }
            else if (responseResult == null || responseResult.Value.IsErr)
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = $"Live LLM request returned error: {responseResult?.Error.Message}";
                if (responseResult != null)
                {
                    result.Details.Add($"Code: {responseResult.Value.Error.Code}");
                    result.Details.Add($"Details: {responseResult.Value.Error.Details}");
                }
            }
            else
            {
                var val = responseResult.Value.Value;
                result.Details.Add($"ProcessingMs: {val.ProcessingMs}ms, Tokens: {val.TokensUsed}");
                result.Details.Add($"Raw Response: {val.Content}");

                // Validate JSON extraction
                string cleanJson = JsonTagExtractor.SanitizeJsonContent(val.Content);
                try
                {
                    var parsed = JObject.Parse(cleanJson);
                    string? status = parsed["status"]?.ToString();
                    if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Status = "PASS";
                        result.PassCount = 1;
                        result.Message = $"Live LLM responded HTTP 200 with valid JSON (tokens={val.TokensUsed}, latency={val.ProcessingMs}ms).";
                    }
                    else
                    {
                        result.Status = "FAIL";
                        result.FailCount = 1;
                        result.Message = $"JSON parsed but 'status' was not 'ok' (got: '{status}').";
                    }
                }
                catch (Exception ex)
                {
                    result.Status = "FAIL";
                    result.FailCount = 1;
                    result.Message = $"JSON parse error: {ex.Message}";
                }
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            RecordSuiteResult(result);
        }

        private IEnumerator RunSuiteContextSnapshot()
        {
            var sw = Stopwatch.StartNew();
            var result = new BehaviorAutotestResult { SuiteId = "Context.SnapshotOrchestration" };

            Pawn? colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
            if (colonist == null)
            {
                result.Status = "SKIP";
                result.Message = "No colonist found on current map to test context building.";
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                RecordSuiteResult(result);
                yield break;
            }

            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var contextEngine = runtimeScope.GetOptional<IContextBuilder>();
            if (contextEngine == null)
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = "IContextBuilder is null in RuntimeServiceHub.";
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                RecordSuiteResult(result);
                yield break;
            }

            string npcId = $"NPC-{colonist.thingIDNumber}";
            Task<ContextSnapshot?> snapshotTask = contextEngine.BuildSnapshotFromEnvelopeAsync(
                npcId, "Autotest colonist inspection", 400, 0.8f, RimMindAPI.Context.ScenarioDecision);

            while (!snapshotTask.IsCompleted)
            {
                yield return null;
            }

            if (snapshotTask.IsFaulted || snapshotTask.Result == null)
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = $"Context snapshot generation failed: {snapshotTask.Exception?.Message}";
            }
            else
            {
                var snapshot = snapshotTask.Result;
                result.Details.Add($"Colonist: {colonist.Name?.ToStringShort} (id={npcId})");
                result.Details.Add($"EstimatedTokens: {snapshot.EstimatedTokens}");
                result.Details.Add($"Messages count: {snapshot.Messages.Count}");
                result.Details.Add($"L0={snapshot.Meta.L0Tokens}, L1={snapshot.Meta.L1Tokens}, L2={snapshot.Meta.L2Tokens}, L3={snapshot.Meta.L3Tokens}, L4={snapshot.Meta.L4Tokens}");

                if (snapshot.Messages.Count >= 2 && snapshot.EstimatedTokens > 0)
                {
                    result.Status = "PASS";
                    result.PassCount = 1;
                    result.Message = $"Context snapshot built successfully ({snapshot.EstimatedTokens} estimated tokens, {snapshot.Messages.Count} messages).";
                }
                else
                {
                    result.Status = "FAIL";
                    result.FailCount = 1;
                    result.Message = $"Context snapshot has insufficient content (messages={snapshot.Messages.Count}, tokens={snapshot.EstimatedTokens}).";
                }
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            RecordSuiteResult(result);
        }

        private IEnumerator RunSuiteAutonomyQueue()
        {
            var sw = Stopwatch.StartNew();
            var result = new BehaviorAutotestResult { SuiteId = "Agent.AutonomyAndApprovalQueue" };

            Pawn? colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
            bool executed = false;

            var entry = new RequestEntry
            {
                source = "BehaviorAutotest",
                pawn = colonist,
                title = "Test Tactical Order",
                description = "Verifying approval queue lifecycle",
                options = new[] { "Approve", "Reject" },
                callback = choice => { executed = true; },
                expireTicks = 5000,
                systemBlocked = false,
            };

            RimMindAPI.RegisterPendingRequest(entry);

            var pending = RimMindAPI.GetPendingRequests();
            if (pending.Contains(entry))
            {
                result.Details.Add("RequestEntry was successfully placed into pending approval queue.");
                entry.TryComplete("Approve", RequestCompletionReason.Selected);
                RimMindAPI.DismissPendingRequest(entry);

                if (executed && !RimMindAPI.GetPendingRequests().Contains(entry))
                {
                    result.Status = "PASS";
                    result.PassCount = 1;
                    result.Message = "Approval queue register, execute, and dismiss flow verified successfully.";
                }
                else
                {
                    result.Status = "FAIL";
                    result.FailCount = 1;
                    result.Message = "Pending request execution or dismissal check failed.";
                }
            }
            else
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = "RegisterPendingRequest did not add entry to pending list.";
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            RecordSuiteResult(result);
            yield return null;
        }

        private IEnumerator RunSuiteFlywheel()
        {
            var sw = Stopwatch.StartNew();
            var result = new BehaviorAutotestResult { SuiteId = "Flywheel.Telemetry" };

            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var collector = runtimeScope.GetOptional<ITelemetryCollector>();
            var store = runtimeScope.GetOptional<IFlywheelParameterStore>();

            if (collector != null && store != null)
            {
                collector.Record("BehaviorAutotestLatency", 120f);
                collector.Record("BehaviorAutotestTokens", 150f);

                var records = collector.GetRecentRecords(3);
                result.Details.Add($"Recent records count: {records.Count}");
                result.Details.Add($"Total budget parameter: {store.TotalBudget}");

                if (records.Count > 0)
                {
                    result.Status = "PASS";
                    result.PassCount = 1;
                    result.Message = "Telemetry recording and parameter store querying verified.";
                }
                else
                {
                    result.Status = "FAIL";
                    result.FailCount = 1;
                    result.Message = "Recorded telemetry record was not retrieved.";
                }
            }
            else
            {
                result.Status = "FAIL";
                result.FailCount = 1;
                result.Message = "TelemetryCollector or FlywheelParameterStore is null.";
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            RecordSuiteResult(result);
            yield return null;
        }

        private IEnumerator RunSuiteAllToolsExecution()
        {
            var sw = Stopwatch.StartNew();
            var suiteResult = new BehaviorAutotestResult { SuiteId = "Tools.InGameExecution" };

            var runtimeScope = RuntimeServiceHub.Shared.Capture();
            var toolRegistry = runtimeScope.GetOptional<IToolRegistry>();
            if (toolRegistry == null)
            {
                suiteResult.Status = "FAIL";
                suiteResult.FailCount = 1;
                suiteResult.Message = "IToolRegistry is null in RuntimeServiceHub.";
                sw.Stop();
                suiteResult.DurationMs = sw.ElapsedMilliseconds;
                RecordSuiteResult(suiteResult);
                yield break;
            }

            var tools = toolRegistry.All;
            suiteResult.Details.Add($"Total registered tools discovered: {tools.Count}");

            Pawn? colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
            VerseMap? map = Find.CurrentMap;

            int passed = 0;
            int domainRejected = 0;
            int failed = 0;

            var toolsReport = new ToolsExecutionReport
            {
                RunId = _runId,
                TotalTools = tools.Count
            };

            foreach (var tool in tools)
            {
                yield return null; // yield a frame between tools for smooth execution and logging
                var item = new ToolExecutionTestItem
                {
                    ToolId = tool.Definition.Id,
                    Category = tool.Definition.Category,
                    Description = tool.Definition.Description
                };

                // 1. Verify Schema
                try
                {
                    if (!string.IsNullOrWhiteSpace(tool.Definition.ParametersSchema))
                    {
                        JObject.Parse(tool.Definition.ParametersSchema);
                        item.SchemaValid = true;
                    }
                }
                catch
                {
                    item.SchemaValid = false;
                }

                // 2. Prepare Valid Arguments
                var args = BuildTestArgumentsForTool(tool, colonist, map);
                item.InputPayload = args.ArgumentsJson;

                // Ensure pawn has a weapon if testing equipment drop
                if (tool.Definition.Id == "pawn.equipment.set" && colonist?.equipment?.Primary == null && colonist != null)
                {
                    var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver") ?? DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                    if (weaponDef != null)
                    {
                        var weapon = (ThingWithComps)ThingMaker.MakeThing(weaponDef);
                        colonist.equipment?.AddEquipment(weapon);
                    }
                }

                // 3. Execute Tool
                var toolSw = Stopwatch.StartNew();
                Task<Result<ToolResult, RimMindError>>? task = null;
                Exception? launchEx = null;
                try
                {
                    task = tool.ExecuteAsync(args, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    launchEx = ex;
                }

                if (task != null)
                {
                    while (!task.IsCompleted)
                    {
                        yield return null;
                    }
                }

                toolSw.Stop();
                item.DurationMs = toolSw.ElapsedMilliseconds;

                if (launchEx != null)
                {
                    item.Status = "FAIL";
                    item.IsError = true;
                    item.ErrorMessage = $"Unhandled Launch Exception: {launchEx.Message}";
                    item.Notes = launchEx.ToString();
                }
                else if (task != null && task.IsFaulted)
                {
                    item.Status = "FAIL";
                    item.IsError = true;
                    item.ErrorMessage = task.Exception?.GetBaseException().Message ?? task.Exception?.Message;
                }
                else if (task != null && task.IsCompleted)
                {
                    var execResult = task.Result;
                    if (execResult.IsErr)
                    {
                        item.Status = "FAIL";
                        item.IsError = true;
                        item.ErrorMessage = $"Result.Err: {execResult.Error.Code} - {execResult.Error.Message}";
                    }
                    else
                    {
                        var res = execResult.Value;
                        item.IsError = res.IsError;
                        item.ReturnSnippet = res.Content != null && res.Content.Length > 200
                            ? res.Content.Substring(0, 200) + "..."
                            : (res.Content ?? "");

                        if (!res.IsError)
                        {
                            item.Status = "PASS";
                            item.Notes = "Executed successfully without error.";
                        }
                        else
                        {
                            item.ErrorMessage = res.Content;
                            if (IsExpectedDomainRejection(tool.Definition.Id, res.Content))
                            {
                                item.Status = "DOMAIN_REJECT";
                                item.Notes = $"Domain rule rejected safely: {res.Content}";
                            }
                            else
                            {
                                item.Status = "FAIL";
                                item.Notes = $"Tool reported error: {res.Content}";
                            }
                        }
                    }
                }

                // Post-execution cleanup if needed
                PostToolCleanup(tool.Definition.Id, colonist);

                // 4. Resilience / Boundary check with invalid pawn_id
                if (tool.Definition.Category == "pawn" || tool.Definition.ParametersSchema.Contains("pawn_id"))
                {
                    var boundaryArgs = new ToolCallArgs
                    {
                        ToolCallId = "boundary-check",
                        ToolName = tool.Definition.Id,
                        ArgumentsJson = "{\"pawn_id\": -9999}",
                        PawnId = -9999
                    };

                    Task<Result<ToolResult, RimMindError>>? boundaryTask = null;
                    try
                    {
                        boundaryTask = tool.ExecuteAsync(boundaryArgs, CancellationToken.None);
                    }
                    catch
                    {
                        item.BoundaryHandledGracefully = false;
                    }

                    if (boundaryTask != null)
                    {
                        while (!boundaryTask.IsCompleted)
                        {
                            yield return null;
                        }
                        item.BoundaryHandledGracefully = !boundaryTask.IsFaulted;
                    }
                }
                else
                {
                    item.BoundaryHandledGracefully = true;
                }

                if (item.Status == "PASS") passed++;
                else if (item.Status == "DOMAIN_REJECT") domainRejected++;
                else failed++;

                toolsReport.Items.Add(item);
                string logMsg = $"[RIMTEST][Tool][{item.ToolId}][{item.Status}] duration={item.DurationMs}ms err={item.ErrorMessage ?? "none"} snippet={item.ReturnSnippet}";
                Log.Message(logMsg);
                suiteResult.Details.Add($"{item.ToolId}: {item.Status} ({item.DurationMs}ms) - {(item.ErrorMessage != null ? item.ErrorMessage : item.Notes)}");
            }

            toolsReport.Passed = passed;
            toolsReport.DomainRejected = domainRejected;
            toolsReport.Failed = failed;
            sw.Stop();
            toolsReport.TotalDurationMs = sw.ElapsedMilliseconds;

            // Save toolsReport to json
            try
            {
                string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "BehaviorTests", _runId);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string toolsReportPath = Path.Combine(dir, "tools-execution-report.json");
                File.WriteAllText(toolsReportPath, JsonConvert.SerializeObject(toolsReport, Formatting.Indented), Encoding.UTF8);
                Log.Message($"[RimMind-Core] Tools execution report written to: {toolsReportPath}");
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Core] Failed to write tools execution report: " + ex);
            }

            suiteResult.PassCount = passed + domainRejected;
            suiteResult.FailCount = failed;
            suiteResult.DurationMs = sw.ElapsedMilliseconds;
            if (failed == 0)
            {
                suiteResult.Status = "PASS";
                suiteResult.Message = $"Tested {tools.Count} tools: {passed} passed directly, {domainRejected} safely rejected by game rules, 0 unhandled failures.";
            }
            else
            {
                suiteResult.Status = "FAIL";
                suiteResult.Message = $"Tested {tools.Count} tools: {passed} passed, {domainRejected} safely rejected, {failed} failed.";
            }

            RecordSuiteResult(suiteResult);
        }

        private ToolCallArgs BuildTestArgumentsForTool(IToolHandler tool, Pawn? pawn, VerseMap? map)
        {
            int pawnId = pawn?.thingIDNumber ?? 0;
            int mapId = map?.uniqueID ?? 0;
            string toolId = tool.Definition.Id;

            var dict = new Dictionary<string, object>();

            if (toolId.StartsWith("pawn.") || tool.Definition.Category == "pawn" || tool.Definition.ParametersSchema.Contains("pawn_id"))
            {
                dict["pawn_id"] = pawnId;
            }
            if (toolId.StartsWith("map.") || tool.Definition.Category == "map" || tool.Definition.ParametersSchema.Contains("map_id"))
            {
                dict["map_id"] = mapId;
            }

            // Specialized parameters for known operations/tools
            switch (toolId)
            {
                case "pawn.job.set":
                    dict["action"] = "cancel_job";
                    break;

                case "pawn.draft.toggle":
                    dict["action"] = "draft";
                    break;

                case "pawn.work.set":
                    dict["def_name"] = "Firefighter";
                    dict["value"] = "3";
                    break;

                case "pawn.equipment.set":
                    dict["action"] = "drop_weapon";
                    break;

                case "pawn.interaction.trigger":
                    dict["action"] = "social_relax";
                    break;

                case "pawn.recruit.trigger":
                    // Recruit is tested on the colonist (will verify domain rejection since already a colonist)
                    break;

                case "pawn.thought.add":
                    dict["def_name"] = "AteWithoutTable";
                    break;

                case "pawn.inspiration.trigger":
                    dict["def_name"] = "Frenzy_Shoot";
                    break;

                case "pawn.mental_state.trigger":
                    dict["def_name"] = "Wander_Sad";
                    break;

                case "pawn.skill.set":
                    dict["def_name"] = "Shooting";
                    dict["action"] = "learn_xp";
                    dict["value"] = "50";
                    break;

                case "pawn.need.set":
                    dict["def_name"] = "Food";
                    dict["action"] = "set_level";
                    dict["value"] = "0.85";
                    break;

                case "world.faction.set":
                    var otherFaction = Find.FactionManager?.AllFactions?
                        .FirstOrDefault(f => !f.def.hidden && f != Faction.OfPlayer);
                    if (otherFaction != null)
                    {
                        dict["params"] = new Dictionary<string, string>
                        {
                            { "target_faction_id", otherFaction.loadID.ToString() },
                            { "goodwill_change", "1" }
                        };
                    }
                    break;

                case "world.storyteller.trigger":
                    dict["def_name"] = "Eclipse";
                    break;

                case "world.choice_letter.trigger":
                    dict["params"] = new Dictionary<string, string>
                    {
                        { "title", "RimMind In-Game Test" },
                        { "description", "Notification letter generated by behavior autotest." }
                    };
                    break;

                case "actions.stabilize_rest":
                    dict["reason"] = "Autotest stabilization";
                    break;
            }

            return new ToolCallArgs
            {
                ToolCallId = $"test-{toolId}-{Guid.NewGuid():N}",
                ToolName = toolId,
                ArgumentsJson = JsonConvert.SerializeObject(dict),
                PawnId = pawnId > 0 ? pawnId : null,
                NpcId = pawnId > 0 ? $"NPC-{pawnId}" : null
            };
        }

        private static void PostToolCleanup(string toolId, Pawn? pawn)
        {
            try
            {
                if (toolId == "pawn.draft.toggle" && pawn?.drafter != null && pawn.Drafted)
                {
                    pawn.drafter.Drafted = false;
                }

                if (toolId == "pawn.mental_state.trigger" && pawn?.mindState?.mentalStateHandler != null && pawn.InMentalState)
                {
                    pawn.mindState.mentalStateHandler.Reset();
                }

                if (toolId == "world.storyteller.trigger" && Find.CurrentMap != null)
                {
                    var eclipseDef = DefDatabase<GameConditionDef>.GetNamedSilentFail("Eclipse");
                    if (eclipseDef != null && Find.CurrentMap.gameConditionManager.ConditionIsActive(eclipseDef))
                    {
                        Find.CurrentMap.gameConditionManager.GetActiveCondition(eclipseDef)?.End();
                    }
                }

                if (toolId == "actions.stabilize_rest" && pawn?.jobs != null && pawn.jobs.curJob?.def == JobDefOf.LayDown)
                {
                    pawn.jobs.EndCurrentJob(global::Verse.AI.JobCondition.InterruptForced);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Core] PostToolCleanup failed for {toolId}: {ex.Message}");
            }
        }

        private static bool IsExpectedDomainRejection(string toolId, string? error)
        {
            if (string.IsNullOrEmpty(error)) return false;

            // pawn.recruit.trigger: pawn is already in player's faction
            if (toolId == "pawn.recruit.trigger" && error.IndexOf("already a colonist", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // pawn.equipment.set: drop_weapon when pawn has no weapon equipped
            if (toolId == "pawn.equipment.set" && error.IndexOf("no weapon equipped", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // pawn.interaction.trigger: social_relax when no joy activity is available
            if (toolId == "pawn.interaction.trigger" && error.IndexOf("no social relax", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // world.storyteller.trigger: conditions for incident not currently met
            if (toolId == "world.storyteller.trigger" && error.IndexOf("Failed to execute incident", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // pawn.job.set: when specific job target not present or cannot work
            if (toolId == "pawn.job.set" && (error.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("No job available", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            return false;
        }

        private void RecordSuiteResult(BehaviorAutotestResult result)
        {
            _report.Suites.Add(result);
            if (result.Status == "PASS") _report.TotalPassed++;
            else if (result.Status == "FAIL") _report.TotalFailed++;

            string outcome = result.Status;
            Log.Message($"[RIMTEST][Behavior][{result.SuiteId}][{outcome}] pass={result.PassCount} fail={result.FailCount} duration={result.DurationMs}ms msg={result.Message}");
        }

        private IEnumerator RunDiscoveredModSuites()
        {
            List<IInGameBehaviorSuite> suites = new();
            try
            {
                var suiteTypes = GenTypes.AllTypes
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IInGameBehaviorSuite).IsAssignableFrom(t));

                foreach (var type in suiteTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IInGameBehaviorSuite suite)
                        {
                            suites.Add(suite);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimMind-Core] Failed to instantiate behavior suite {type.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Core] Failed to discover behavior suites: {ex.Message}");
            }

            Log.Message($"[RimMind-Core] Discovered {suites.Count} submodule behavior autotest suites.");

            Pawn? colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
            VerseMap? map = Find.CurrentMap;

            foreach (var suite in suites)
            {
                var sw = Stopwatch.StartNew();
                var result = new BehaviorAutotestResult
                {
                    SuiteId = $"{suite.ModId}.{suite.SuiteId}"
                };

                var ctx = new InGameBehaviorSuiteContext(colonist, map);

                try
                {
                    suite.RunSuite(ctx);
                    result.PassCount = ctx.PassCount;
                    result.FailCount = ctx.FailCount;
                    result.Details.AddRange(ctx.Details);

                    if (ctx.FailCount == 0 && ctx.PassCount > 0)
                    {
                        result.Status = "PASS";
                        result.Message = $"All {ctx.PassCount} checks passed for {suite.SuiteId}.";
                    }
                    else if (ctx.FailCount > 0)
                    {
                        result.Status = "FAIL";
                        result.Message = $"{ctx.FailCount} checks failed out of {ctx.PassCount + ctx.FailCount}.";
                    }
                    else
                    {
                        result.Status = "PASS";
                        result.Message = $"Suite {suite.SuiteId} executed with no checks failed.";
                    }
                }
                catch (Exception ex)
                {
                    result.FailCount = Math.Max(1, ctx.FailCount);
                    result.Status = "FAIL";
                    result.Message = $"Exception in {suite.SuiteId}: {ex.Message}";
                    result.Details.Add(ex.ToString());
                }

                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                RecordSuiteResult(result);
                yield return null;
            }
        }

        private void FinalizeReport()
        {
            _clock.Stop();
            _report.TotalDurationMs = _clock.ElapsedMilliseconds;
            _report.OverallStatus = _report.TotalFailed == 0 ? "PASS" : "FAIL";

            string logSummary = $"[RIMTEST][Behavior][Summary] Status={_report.OverallStatus} TotalSuites={_report.Suites.Count} Passed={_report.TotalPassed} Failed={_report.TotalFailed} TotalDuration={_report.TotalDurationMs}ms";
            if (_report.TotalFailed == 0) Log.Message(logSummary);
            else Log.Error(logSummary);

            // Write report JSON
            try
            {
                string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimMind", "BehaviorTests", _runId);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string reportPath = Path.Combine(dir, "behavior-test-report.json");
                File.WriteAllText(reportPath, JsonConvert.SerializeObject(_report, Formatting.Indented), Encoding.UTF8);
                Log.Message($"[RimMind-Core] Behavior autotest report written to: {reportPath}");
            }
            catch (Exception ex)
            {
                Log.Error("[RimMind-Core] Failed to write behavior test report: " + ex);
            }

            if (_isHeadless)
            {
                Log.Message("[RimMind-Core] Headless behavior autotest completed. Shutting down game process...");
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    Root.Shutdown();
                });
            }
            else
            {
                Messages.Message($"RimMind Behavior Autotests: {_report.OverallStatus} ({_report.TotalPassed} passed, {_report.TotalFailed} failed)",
                    _report.TotalFailed == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
            }

            _active = null;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }
    }
}
