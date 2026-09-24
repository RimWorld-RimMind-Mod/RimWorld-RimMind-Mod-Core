using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Features.Storage;
using RimMind.Domain.Common;
using RimMind.Domain.Settings;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class PublicApiContracts
    {
        [Fact]
        public void Public_facades_follow_lifecycle_without_exposing_the_container()
        {
            ContractCaseRunner.Run(
                ("single-service facades follow the current generation", () =>
                {
                    var api = ReadApiTree();
                    Assert.Contains("RuntimeServiceRef<", api, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindRuntime.Instance", api, StringComparison.Ordinal);
                }),
                ("request facade delegates to one application boundary", () =>
                {
                    var facade = ReadSource("Presentation/Api/RimMindAPI.Request.cs");
                    Assert.Contains("RuntimeServiceRef<IRequestSubmissionService>", facade, StringComparison.Ordinal);
                    Assert.DoesNotContain("RuntimeServiceHub", facade, StringComparison.Ordinal);
                    Assert.DoesNotContain("IClientManager", facade, StringComparison.Ordinal);
                    Assert.DoesNotContain("IPipeline<LlmRequestContext>", facade, StringComparison.Ordinal);
                    Assert.DoesNotContain("RequestCancellationRegistrations", facade, StringComparison.Ordinal);
                }),
                ("multi-service agent facade uses one coherent scope", () =>
                {
                    var source = ReadSource("Presentation/Api/RimMindAPI.Agents.cs");
                    Assert.Contains("RuntimeServiceHub.Shared.Capture", source, StringComparison.Ordinal);
                    Assert.Contains("scope.GetOptional<IScopedAgentManager>()", source, StringComparison.Ordinal);
                    Assert.Contains("scope.GetOptional<IAgentBus>()", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("Buses.ValueOrDefault", source, StringComparison.Ordinal);
                }),
                ("request submission owns pipeline execution and cancellation", () =>
                {
                    var submission = ReadSource(
                        "Application/Features/Requests/RequestSubmissionService.cs");
                    Assert.Contains("RequestCancellationRegistrations.TryCreate", submission, StringComparison.Ordinal);
                    Assert.Contains("_completionFence.CancellationToken", submission, StringComparison.Ordinal);
                    Assert.Contains("QueuedPipelineRequestExecutor", submission, StringComparison.Ordinal);
                }),
                ("composition root binds the request submission boundary", () =>
                {
                    var composition = ReadSource(
                        "Presentation/Runtime/RimMindCompositionRoot.cs");
                    Assert.Contains("Bind<IRequestSubmissionService>", composition, StringComparison.Ordinal);
                }),
                ("request queue lives beside the submission boundary", () =>
                {
                    var root = SourceRoot();
                    var queuePath = Path.Combine(
                        root, "Application", "Features", "Requests", "Queue", "RequestQueue.cs");
                    Assert.True(File.Exists(queuePath));
                    var oldQueueDirectory = Path.Combine(
                        root, "Application", "Features", "Queue");
                    Assert.True(
                        !Directory.Exists(oldQueueDirectory) ||
                        !Directory.EnumerateFiles(oldQueueDirectory, "*.cs").Any());
                    Assert.False(File.Exists(Path.Combine(
                        root, "Presentation", "Runtime", "AIRequestQueue.cs")));
                    Assert.Contains(
                        "RequestQueue : ITickableRequestQueue",
                        File.ReadAllText(queuePath),
                        StringComparison.Ordinal);
                }),
                ("remote sync pull preserves cancellation as an error", AssertRemotePullCancellation),
                ("remote sync push preserves cancellation as an error", AssertRemotePushCancellation),
                ("remote sync load delegates to the current generation", () =>
                {
                    var source = ReadSource("Presentation/Api/RimMindAPI.RemoteSync.cs");
                    Assert.Contains("SyncOnLoadAsync", source, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceHub.Shared.Capture", source, StringComparison.Ordinal);
                }),
                ("remote sync push delegates to the current generation", () =>
                {
                    var source = ReadSource("Presentation/Api/RimMindAPI.RemoteSync.cs");
                    Assert.Contains("EnqueuePushAsync", source, StringComparison.Ordinal);
                    Assert.Contains("IRemoteSyncService", source, StringComparison.Ordinal);
                }),
                ("remote sync is unavailable while runtime is stopped", () =>
                {
                    var source = ReadSource("Presentation/Api/RimMindAPI.RemoteSync.cs");
                    Assert.Contains("Result<", source, StringComparison.Ordinal);
                    Assert.Contains("RimMindError", source, StringComparison.Ordinal);
                    Assert.Contains("RuntimeLifecycleState.Running", source, StringComparison.Ordinal);
                }),
                ("public signatures do not expose lifecycle infrastructure", () =>
                {
                    var publicLines = ReadApiTree().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => line.Contains("public ", StringComparison.Ordinal));
                    Assert.DoesNotContain(publicLines, line =>
                        line.Contains("RuntimeServiceHub", StringComparison.Ordinal) ||
                        line.Contains("RuntimeServiceScope", StringComparison.Ordinal) ||
                        line.Contains("RuntimeGenerationToken", StringComparison.Ordinal));
                }),
                ("Core Mod is the sole lifecycle host initializer", () =>
                {
                    var source = ReadSource("AICoreMod.cs");
                    Assert.Contains("RimMindRuntimeHost.Initialize", source, StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceHub.Shared.Capture", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindRuntime.Instance", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindServiceLocator", source, StringComparison.Ordinal);
                }),
                ("long-lived Core extensions follow recomposed settings", () =>
                {
                    var source = ReadSource("AICoreMod.cs");
                    Assert.Contains("RuntimeServiceRef<ISettingsProvider>", source, StringComparison.Ordinal);
                    Assert.Contains("new CoreOverlayToggle()", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("new CoreOverlayToggle(sp)", source, StringComparison.Ordinal);
                }));
        }

        private static string ReadApiTree()
        {
            var directory = Path.Combine(SourceRoot(), "Presentation", "Api");
            return string.Join("\n", Directory.GetFiles(directory, "RimMindAPI*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void AssertRemotePullCancellation()
        {
            var orchestrator = new RemoteSyncOrchestrator(
                new CancellingRemoteBackend(),
                new RemoteSyncSettings { AutoPull = true });

            var result = orchestrator
                .SyncOnLoadAsync(RemoteKeys.ContextSettings(), 0, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.Cancelled, result.Error.Code);
        }

        private static void AssertRemotePushCancellation()
        {
            var orchestrator = new RemoteSyncOrchestrator(
                new CancellingRemoteBackend(),
                new RemoteSyncSettings { AutoPush = true, PushDebounceSeconds = 0 });

            var result = orchestrator
                .EnqueuePushAsync(RemoteKeys.ContextSettings(), "{}", 1, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.Cancelled, result.Error.Code);
        }

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }

        private sealed class CancellingRemoteBackend : IRemoteBackend
        {
            public string ProviderName => "contracts";
            public bool IsConfigured => true;

            public Task<Result<RemoteEntry?, RimMindError>> PullAsync(
                string key,
                CancellationToken ct) =>
                Task.FromException<Result<RemoteEntry?, RimMindError>>(
                    new OperationCanceledException(ct));

            public Task<Result<bool, RimMindError>> PushAsync(
                string key,
                string json,
                long localVersion,
                CancellationToken ct) =>
                Task.FromException<Result<bool, RimMindError>>(
                    new OperationCanceledException(ct));

            public Task<Result<bool, RimMindError>> DeleteAsync(
                string key,
                CancellationToken ct) =>
                Task.FromException<Result<bool, RimMindError>>(
                    new OperationCanceledException(ct));
        }
    }
}
