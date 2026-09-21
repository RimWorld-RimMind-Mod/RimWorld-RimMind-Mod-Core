using System;
using System.IO;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.UI.AgentFlow;
using RimMind.Infrastructure.UI.AgentStatePreview;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Framework;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class UiLifecycleContract
    {
        [Fact]
        public void Ui_adapters_rebind_and_fence_runtime_state_by_generation()
        {
            ContractCaseRunner.Run(
                ("simple ui adapters use generation aware service refs", () =>
                {
                    Assert.Contains("RuntimeServiceRef<IOverlayService>", ReadSource("Infrastructure/UI/RequestOverlay.cs"), StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceRef<IRequestQueue>", ReadSource("Presentation/UI/QueueTabDrawer.cs"), StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceRef<IExtensionRegistry<ISettingsTab>>", ReadSource("Presentation/UI/AICoreSettingsUI.cs"), StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceRef<IAgentIdentityProvider>", ReadSource("Infrastructure/Patches/FloatMenu_InnerVoice.cs"), StringComparison.Ordinal);
                }),
                ("debug center binds all derived drawers from one captured scope", () =>
                {
                    var hub = ReadSource("Infrastructure/UI/MainTabWindow_RimMindHub.cs");
                    Assert.Contains("RuntimeBinding", hub, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceScope", hub, StringComparison.Ordinal);
                    Assert.Contains("PreClose", hub, StringComparison.Ordinal);
                    Assert.Contains("_runtimeBinding.Dispose", hub, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindServiceLocator", ReadSource("Infrastructure/UI/DebugCenter/DebugCenterPageRegistry.cs"), StringComparison.Ordinal);
                }),
                ("runtime data caches include generation while visual state survives", () =>
                {
                    var requests = ReadSource("Infrastructure/UI/DebugCenter/Pages/AIRequestsDebugCenterPageDrawer.cs");
                    Assert.Contains("_cachedGeneration", requests, StringComparison.Ordinal);
                    Assert.Contains("scope.Generation", requests, StringComparison.Ordinal);
                    Assert.Contains("_tableScrollPosition", requests, StringComparison.Ordinal);
                    Assert.Contains("_detailScrollPosition", requests, StringComparison.Ordinal);
                }),
                ("ai request selection resets by generation while scroll remains visual state", () =>
                {
                    var state = new GenerationSelectionState<string>();
                    var tableScroll = 19f;
                    var detailScroll = 23f;

                    Assert.True(state.Refresh(4));
                    state.Select("request-a");
                    Assert.Equal("request-a", state.Selection);
                    Assert.False(state.Refresh(4));

                    Assert.True(state.Refresh(5));
                    Assert.Null(state.Selection);
                    Assert.Equal(19f, tableScroll);
                    Assert.Equal(23f, detailScroll);
                }),
                ("generation state invalidates derived values without touching visual state", () =>
                {
                    var state = new GenerationUiState();
                    var scroll = 37f;
                    var selectedTab = "queue";
                    var explicitlyVisible = true;
                    var temporarilyClosed = true;

                    Assert.True(state.Refresh(1));
                    state.MarkDerivedState();
                    state.MarkInteractionActive();
                    Assert.False(state.Refresh(1));
                    Assert.True(state.HasDerivedState);
                    Assert.True(state.HasActiveInteraction);

                    Assert.True(state.Refresh(2));
                    Assert.False(state.HasDerivedState);
                    Assert.False(state.HasActiveInteraction);
                    Assert.Equal(37f, scroll);
                    Assert.Equal("queue", selectedTab);
                    Assert.True(explicitlyVisible);
                    Assert.True(temporarilyClosed);
                }),
                ("stale ui operation rejects every publication and records one discard", () =>
                {
                    var hub = new RuntimeServiceHub();
                    var operation = new GenerationUiOperation(
                        hub,
                        new RuntimeGenerationToken(Guid.NewGuid(), 4),
                        LifecycleEventSources.TestConnection);

                    Assert.False(operation.CanPublish());
                    Assert.False(operation.CanPublish());
                    Assert.Equal(1, hub.GetDiagnostics().StaleCompletionDiscardCount);
                }),
                ("settings operation resolves every service from one captured scope", () =>
                {
                    var hub = new RuntimeServiceHub();
                    PublishUiProbe(hub, 1);
                    var operation = GenerationUiOperation.Capture(
                        hub,
                        LifecycleEventSources.SettingsUi);

                    PublishUiProbe(hub, 2);

                    Assert.Equal(1, operation.Scope.GetRequired<IUiGenerationProbe>().Generation);
                    Assert.False(operation.CanPublish());
                }),
                ("old rendered overlay operation is rejected once after replacement", () =>
                {
                    var hub = new RuntimeServiceHub();
                    PublishUiProbe(hub, 1);
                    var operation = GenerationUiOperation.Capture(
                        hub,
                        LifecycleEventSources.RequestOverlay);

                    PublishUiProbe(hub, 2);

                    Assert.False(operation.CanPublish());
                    Assert.False(operation.CanPublish());
                    Assert.Equal(1, hub.GetDiagnostics().StaleCompletionDiscardCount);
                }),
                ("async ui completions carry and validate runtime tokens", () =>
                {
                    AssertAsyncFence("Infrastructure/UI/AgentFlow/AgentFlowAsyncCoordinator.cs");
                    AssertAsyncFence("Infrastructure/UI/AgentState/AgentContextPreviewCoordinator.cs");
                    var dialogue = ReadSource("Infrastructure/UI/Window_AgentDialogue.cs");
                    Assert.Contains("RuntimeGenerationToken", dialogue, StringComparison.Ordinal);
                    Assert.Contains("IsCurrent", dialogue, StringComparison.Ordinal);
                }),
                ("stale context preview completion enters an explicit terminal state", () =>
                {
                    var hub = new RuntimeServiceHub();
                    var coordinator = new AgentContextPreviewCoordinator(hub);
                    coordinator.Begin(
                        Task.FromResult<ContextSnapshot?>(new ContextSnapshot()),
                        "loading",
                        new RuntimeGenerationToken(Guid.NewGuid(), 1));

                    coordinator.Poll("discarded", _ => "must not publish");

                    Assert.Equal("Discarded", coordinator.State.ToString());
                    Assert.Equal("discarded", coordinator.Summary);
                    Assert.Equal(1, hub.GetDiagnostics().StaleCompletionDiscardCount);
                }),
                ("stale agent flow completions clear pending state and report terminal discard", () =>
                {
                    var hub = new RuntimeServiceHub();
                    var token = new RuntimeGenerationToken(Guid.NewGuid(), 1);
                    var coordinator = new AgentFlowAsyncCoordinator(hub);
                    coordinator.BeginContextBuild(
                        Task.FromResult<ContextSnapshot?>(new ContextSnapshot()),
                        token);

                    Assert.True(coordinator.PollContextBuild(out var snapshot, out var contextError));
                    Assert.Null(snapshot);
                    Assert.Equal("RimMind.UI.Lifecycle.StaleCompletion", contextError);

                    var executionContext = new AgentFlowExecutionContext(
                        7,
                        "Pawn",
                        "NPC-7",
                        "pawn.job.force_rest",
                        MechanismOperationType.Set);
                    coordinator.BeginMechanismExecution(
                        Task.FromResult(Result<bool, RimMindError>.Ok(true)),
                        executionContext,
                        token);

                    Assert.True(coordinator.PollMechanismExecution(out var completion));
                    Assert.NotNull(completion);
                    Assert.Null(completion!.Result);
                    Assert.Equal("RimMind.UI.Lifecycle.StaleCompletion", completion.Error);
                    Assert.False(coordinator.HasPendingMechanismExecution);
                    Assert.Equal(2, hub.GetDiagnostics().StaleCompletionDiscardCount);
                }),
                ("stable agent identity resolves the replacement current agent", () =>
                {
                    object currentAgent = new object();
                    var binding = new CurrentAgentBinding<object>(() => currentAgent);
                    object firstAgent = binding.Resolve()!;

                    currentAgent = new object();

                    Assert.NotSame(firstAgent, binding.Resolve());
                    Assert.Same(currentAgent, binding.Resolve());
                }),
                ("agent flow publication requires runtime and target generation and resets derived state", () =>
                {
                    var currentToken = new RuntimeGenerationToken(Guid.NewGuid(), 1);
                    var state = new AgentFlowGenerationState();

                    Assert.True(state.Refresh(currentToken, 3));
                    state.MarkDerivedState();
                    Assert.True(state.CanPublish(currentToken, 3, token => token == currentToken));
                    Assert.False(state.CanPublish(currentToken, 4, token => token == currentToken));

                    var replacementToken = new RuntimeGenerationToken(Guid.NewGuid(), 2);
                    Assert.False(state.CanPublish(currentToken, 3, token => token == replacementToken));

                    Assert.True(state.Refresh(replacementToken, 3));
                    Assert.False(state.HasDerivedState);

                    var tracker = new AgentFlowStepTracker();

                    foreach (FlowLabStep step in Enum.GetValues(typeof(FlowLabStep)))
                        Assert.Equal(StepStatus.Pending, tracker.Get(step));

                    tracker.Set(FlowLabStep.SendRequest, StepStatus.Active);
                    tracker.Set(FlowLabStep.SendRequest, StepStatus.Failed);

                    Assert.Equal(StepStatus.Failed, tracker.Get(FlowLabStep.SendRequest));

                    tracker.Reset();

                    Assert.Equal(StepStatus.Pending, tracker.Get(FlowLabStep.SendRequest));
                }),
                ("live agent flow request polls its runtime fence into a localized terminal state", () =>
                {
                    var flowLab = ReadAgentFlowLabSource();
                    Assert.Contains("_liveRequestToken", flowLab, StringComparison.Ordinal);
                    Assert.Contains("CompleteStaleLiveRequest", flowLab, StringComparison.Ordinal);
                    Assert.Contains("TryAcceptLiveRequest", flowLab, StringComparison.Ordinal);
                    Assert.Contains(
                        "_requestStatus = \"RimMind.UI.Lifecycle.StaleCompletion\".Translate()",
                        flowLab,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "_stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Failed)",
                        flowLab,
                        StringComparison.Ordinal);
                }),
                ("stale mechanism target clears execute active state with the localized terminal", () =>
                {
                    var flowLab = ReadAgentFlowLabSource();
                    int staleTargetBranch = flowLab.IndexOf(
                        "execution.Context.TargetGeneration != _targetGeneration",
                        StringComparison.Ordinal);
                    Assert.True(staleTargetBranch >= 0);
                    string branchTail = flowLab.Substring(
                        staleTargetBranch,
                        Math.Min(500, flowLab.Length - staleTargetBranch));
                    Assert.Contains(
                        "\"RimMind.UI.Lifecycle.StaleCompletion\".Translate()",
                        branchTail,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "_stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed)",
                        branchTail,
                        StringComparison.Ordinal);
                }),
                ("dialogue owns one active request identity and records stale streaming once", () =>
                {
                    var dialogue = ReadSource("Infrastructure/UI/Window_AgentDialogue.cs");
                    Assert.Contains("DialogueRequestState", dialogue, StringComparison.Ordinal);
                    Assert.Contains("_activeRequest", dialogue, StringComparison.Ordinal);
                    Assert.Contains("ReferenceEquals(_activeRequest, request)", dialogue, StringComparison.Ordinal);
                    Assert.Contains("RecordStaleOnce", dialogue, StringComparison.Ordinal);
                    Assert.Contains("_isStreaming = false", dialogue, StringComparison.Ordinal);
                    Assert.Contains("_activeRequest = null", dialogue, StringComparison.Ordinal);
                    Assert.Contains("DialogueRequestTerminalState", dialogue, StringComparison.Ordinal);
                    Assert.Contains(
                        "_lastRequestState = DialogueRequestTerminalState.Discarded",
                        dialogue,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "\"RimMind.UI.AgentDialogue.RequestStatus\".Translate",
                        dialogue,
                        StringComparison.Ordinal);
                    Assert.Contains("LocalizeRequestState", dialogue, StringComparison.Ordinal);
                }),
                ("pawn agent creation resolves one current runtime scope", () =>
                {
                    var pawnTab = ReadSource("Infrastructure/Verse/ITab_Pawn_Agent.cs");
                    Assert.DoesNotContain("RimMindServiceLocator", pawnTab, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceScope runtimeScope", pawnTab, StringComparison.Ordinal);
                    Assert.Contains("IsCurrent(runtimeScope.Token)", pawnTab, StringComparison.Ordinal);
                }),
                ("connection tests fence both success and failure publication", () =>
                {
                    var connection = ReadSource("Presentation/UI/ApiTabDrawer.TestConnection.cs");
                    Assert.Contains("ConnectionTestOperation", connection, StringComparison.Ordinal);
                    Assert.Contains("runtimeScope.Token", connection, StringComparison.Ordinal);
                    Assert.Contains("TryPublishConnectionTest", connection, StringComparison.Ordinal);
                    Assert.Contains("GenerationUiOperation", connection, StringComparison.Ordinal);
                    Assert.Contains("operation.CanPublish()", connection, StringComparison.Ordinal);
                    Assert.Contains("_testPending = false", connection, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        "_testStatus = \"RimMind.UI.Lifecycle.StaleCompletion\"",
                        connection,
                        StringComparison.Ordinal);
                }),
                ("api drawer resolves one required provider registry per draw", () =>
                {
                    var api = ReadSource("Presentation/UI/ApiTabDrawer.cs");
                    Assert.Contains(
                        "RuntimeServiceRef<IExtensionRegistry<IAIClientFactory>>",
                        api,
                        StringComparison.Ordinal);
                    Assert.Contains("ProviderRegistry.Resolve(runtimeScope)", api, StringComparison.Ordinal);
                    Assert.Contains(
                        "AIProviderRegistry.GetAllProviderIds(providerRegistry)",
                        api,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry)",
                        api,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "QuickApplyOpenCodeGo",
                        api,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "DrawExtendedServiceSection(listing, s, scope)",
                        api,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "OpenProviderSelectionMenu(s, providerRegistry, player2Lifecycle)",
                        api,
                        StringComparison.Ordinal);
                }),
                ("settings window follows the current settings provider", () =>
                {
                    var window = ReadSource("Infrastructure/UI/Window_RimMindSettings.cs");
                    Assert.Contains("RuntimeServiceRef<ISettingsProvider>", window, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceRef<ISettingsProvider>.Required()", window, StringComparison.Ordinal);
                    Assert.Contains("_settingsProvider.Value", window, StringComparison.Ordinal);
                    Assert.DoesNotContain("private readonly ISettingsProvider _settingsProvider", window, StringComparison.Ordinal);
                }),
                ("context budget exposes only the effective setting, reads legacy weight keys, and drops orphaned labels", () =>
                {
                    var budgetContract = ReadSource("Application/Common/Interfaces/Internal/IContextBudgetSettings.cs");
                    var contextSettings = ReadSource("Presentation/Settings/ContextSettings.cs");
                    var settingsProvider = ReadSource("Presentation/Settings/SettingsProvider.ContextInclude.cs");
                    var queueDefaults = ReadSource(
                        "Application/Features/Requests/Queue/QueueDefaultSettings.cs");
                    var contextDrawer = ReadSource("Presentation/UI/ContextTabDrawer.cs");
                    var englishKeyed = ReadCoreFile("Languages/English/Keyed/RimMind_Core.xml");
                    var chineseKeyed = ReadCoreFile("Languages/ChineseSimplified/Keyed/RimMind_Core.xml");

                    Assert.DoesNotContain("BudgetW1", budgetContract, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW2", budgetContract, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW1", settingsProvider, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW2", settingsProvider, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW1", queueDefaults, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW2", queueDefaults, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW1", contextDrawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("BudgetW2", contextDrawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMind.Context.BudgetW1", englishKeyed, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMind.Context.BudgetW2", englishKeyed, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMind.Context.BudgetW1", chineseKeyed, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMind.Context.BudgetW2", chineseKeyed, StringComparison.Ordinal);
                    Assert.DoesNotContain("public float BudgetW1", contextSettings, StringComparison.Ordinal);
                    Assert.DoesNotContain("public float BudgetW2", contextSettings, StringComparison.Ordinal);
                    Assert.Contains("private float _legacyBudgetW1", contextSettings, StringComparison.Ordinal);
                    Assert.Contains("private float _legacyBudgetW2", contextSettings, StringComparison.Ordinal);

                    int loadingGuard = contextSettings.IndexOf(
                        "if (Scribe.mode == LoadSaveMode.LoadingVars)",
                        StringComparison.Ordinal);
                    int legacyW1Read = contextSettings.IndexOf(
                        "Scribe_Values.Look(ref _legacyBudgetW1, \"BudgetW1\"",
                        StringComparison.Ordinal);
                    int legacyW2Read = contextSettings.IndexOf(
                        "Scribe_Values.Look(ref _legacyBudgetW2, \"BudgetW2\"",
                        StringComparison.Ordinal);

                    Assert.True(loadingGuard >= 0);
                    Assert.True(legacyW1Read > loadingGuard);
                    Assert.True(legacyW2Read > legacyW1Read);
                    Assert.Equal(
                        contextSettings.IndexOf("\"BudgetW1\"", StringComparison.Ordinal),
                        contextSettings.LastIndexOf("\"BudgetW1\"", StringComparison.Ordinal));
                    Assert.Equal(
                        contextSettings.IndexOf("\"BudgetW2\"", StringComparison.Ordinal),
                        contextSettings.LastIndexOf("\"BudgetW2\"", StringComparison.Ordinal));
                }),
                ("missing replacement settings tab falls back to api", () =>
                {
                    var settings = ReadSource("Presentation/UI/AICoreSettingsUI.cs");
                    Assert.Contains("EnsureCurrentTab", settings, StringComparison.Ordinal);
                    Assert.Contains("_curTab = \"api\"", settings, StringComparison.Ordinal);
                }),
                ("npc sync state is isolated and fenced by generation and npc", () =>
                {
                    var sync = ReadSource("Infrastructure/UI/NpcSyncActions.cs");
                    Assert.Contains("SyncOperationKey", sync, StringComparison.Ordinal);
                    Assert.Contains("RuntimeGenerationToken", sync, StringComparison.Ordinal);
                    Assert.Contains("runtimeScope.Generation", sync, StringComparison.Ordinal);
                    Assert.Contains("RecordStaleOnce", sync, StringComparison.Ordinal);
                    Assert.Contains("ReferenceEquals", sync, StringComparison.Ordinal);
                }),
                ("tool execution publishes from one fenced runtime scope", () =>
                {
                    var tool = ReadSource("Infrastructure/UI/Window_ToolCallDebug.cs");
                    Assert.Contains("RuntimeServiceScope runtimeScope", tool, StringComparison.Ordinal);
                    Assert.Contains("runtimeScope.GetOptional<IToolRegistry>()", tool, StringComparison.Ordinal);
                    Assert.Contains("ToolExecutionOperation", tool, StringComparison.Ordinal);
                    Assert.Contains("TryPublishExecution", tool, StringComparison.Ordinal);
                    Assert.Contains("RecordStaleOnce", tool, StringComparison.Ordinal);
                    Assert.Contains("RimMind.UI.Lifecycle.StaleCompletion", tool, StringComparison.Ordinal);
                }),
                ("context key selection resets when runtime generation changes", () =>
                {
                    var context = ReadSource("Infrastructure/UI/Window_ContextKeyDebug.cs");
                    Assert.Contains("_selectionGeneration", context, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceHub.Shared.Capture()", context, StringComparison.Ordinal);
                    Assert.Contains("RefreshGeneration(runtimeScope.Generation)", context, StringComparison.Ordinal);
                    Assert.Contains("_selectedKeyDetail = string.Empty", context, StringComparison.Ordinal);
                }),
                ("ctrl takes precedence over shift for the core icon", () =>
                {
                    var patch = ReadSource("Infrastructure/Patches/RimMindPlaySettingsPatch.cs");
                    int controlBranch = patch.IndexOf("if (control)", StringComparison.Ordinal);
                    int shiftBranch = patch.IndexOf("if (shift)", StringComparison.Ordinal);
                    Assert.True(controlBranch >= 0);
                    Assert.True(shiftBranch >= 0);
                    Assert.True(controlBranch < shiftBranch);
                }),
                ("dialogue replaces its exact stable turn and exposes enter focus", () =>
                {
                    var dialogue = ReadSource("Infrastructure/UI/Window_AgentDialogue.cs");
                    Assert.Contains("DialogueTurnId", dialogue, StringComparison.Ordinal);
                    Assert.Contains("Guid.NewGuid().ToString(\"N\")", dialogue, StringComparison.Ordinal);
                    Assert.Contains("CreatePlaceholder(request.TurnId)", dialogue, StringComparison.Ordinal);
                    Assert.Contains("ReplaceAssistantTurnById", dialogue, StringComparison.Ordinal);
                    Assert.Contains("AddPendingTurn", dialogue, StringComparison.Ordinal);
                    Assert.Contains("ReplaceAssistantTurn", dialogue, StringComparison.Ordinal);
                    Assert.Contains("RemoveTurn", dialogue, StringComparison.Ordinal);
                    Assert.Contains("DialogueHistoryProjection.Remove", dialogue, StringComparison.Ordinal);
                    Assert.DoesNotContain("ReplaceLastAssistantTurn", dialogue, StringComparison.Ordinal);
                    Assert.Contains("GUI.SetNextControlName(\"AgentDialogueInput\")", dialogue, StringComparison.Ordinal);
                    Assert.DoesNotContain("prevText", dialogue, StringComparison.Ordinal);
                }),
                ("npc sync prunes generations and always cleans completed operations", () =>
                {
                    var sync = ReadSource("Infrastructure/UI/NpcSyncActions.cs");
                    Assert.Contains("PruneStaleGenerations", sync, StringComparison.Ordinal);
                    Assert.Contains("MaxRetainedStates", sync, StringComparison.Ordinal);
                    Assert.Contains("CancellationTokenSource", sync, StringComparison.Ordinal);
                    Assert.Contains(
                        "ManualPullAsync(operation.Key.NpcId, operation.Cancellation.Token)",
                        sync,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "EnqueuePushAsync(operation.Key.NpcId, \"\", 0, operation.Cancellation.Token)",
                        sync,
                        StringComparison.Ordinal);
                    Assert.Contains("finally", sync, StringComparison.Ordinal);
                    Assert.Contains("CleanupOperation", sync, StringComparison.Ordinal);
                }),
                ("dialogue reserves the measured npc sync height", () =>
                {
                    var dialogue = ReadSource("Infrastructure/UI/Window_AgentDialogue.cs");
                    var sync = ReadSource("Infrastructure/UI/NpcSyncActions.cs");
                    Assert.Contains("NpcSyncActions.MeasureHeight", dialogue, StringComparison.Ordinal);
                    Assert.Contains("public static float MeasureHeight", sync, StringComparison.Ordinal);
                    Assert.DoesNotContain("float syncAreaHeight = 34f", dialogue, StringComparison.Ordinal);
                }));
        }

        private static void AssertAsyncFence(string relativePath)
        {
            var source = ReadSource(relativePath);
            Assert.Contains("RuntimeGenerationToken", source, StringComparison.Ordinal);
            Assert.Contains("IsCurrent", source, StringComparison.Ordinal);
            Assert.Contains("RecordStaleCompletion", source, StringComparison.Ordinal);
        }

        private static void PublishUiProbe(RuntimeServiceHub hub, int generation)
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<IUiGenerationProbe>(new UiGenerationProbe(generation));
            builder.Require<IUiGenerationProbe>();
            var snapshot = builder.Build();
            var lifetime = new RuntimeLifetime(snapshot.RuntimeId, hub.IsCurrent, hub.RecordStaleCompletion);
            hub.Publish(snapshot, lifetime);
        }

        private interface IUiGenerationProbe
        {
            int Generation { get; }
        }

        private sealed class UiGenerationProbe : IUiGenerationProbe
        {
            public UiGenerationProbe(int generation)
            {
                Generation = generation;
            }

            public int Generation { get; }
        }

        private static string ReadAgentFlowLabSource() => string.Concat(
            ReadSource("Infrastructure/UI/Window_AgentFlowLab.cs"),
            ReadSource("Infrastructure/UI/AgentFlow/Window_AgentFlowLab.Layout.cs"),
            ReadSource("Infrastructure/UI/AgentFlow/Window_AgentFlowLab.Target.cs"),
            ReadSource("Infrastructure/UI/AgentFlow/Window_AgentFlowLab.Request.cs"),
            ReadSource("Infrastructure/UI/AgentFlow/Window_AgentFlowLab.Mechanism.cs"),
            ReadSource("Infrastructure/UI/AgentFlow/Window_AgentFlowLab.Diagnostics.cs"));

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string ReadCoreFile(string relativePath) =>
            File.ReadAllText(Path.Combine(
                Directory.GetParent(SourceRoot())?.FullName ?? throw new InvalidOperationException("Core root not found."),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }
    }
}
