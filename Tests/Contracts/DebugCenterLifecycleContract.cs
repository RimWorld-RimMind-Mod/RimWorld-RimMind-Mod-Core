using System;
using System.IO;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Framework;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class DebugCenterLifecycleContract
    {
        [Fact]
        public void Existing_debug_center_exposes_runtime_and_game_lifecycle_diagnostics()
        {
            ContractCaseRunner.Run(
                ("overview shows complete runtime and game diagnostics", () =>
                {
                    var model = ReadSource("Infrastructure/UI/DebugCenter/Overview/DebugCenterOverviewModel.cs");
                    Assert.Contains("RuntimeLifecycleDiagnostics", model, StringComparison.Ordinal);
                    Assert.Contains("GameLifecycleDiagnostics", model, StringComparison.Ordinal);
                    foreach (var field in new[] { "Generation", "ServiceCount", "PublishedAtUtc", "RuntimeId", "LastBuildFailureSummary", "StaleCompletionDiscardCount" })
                        Assert.Contains(field, model, StringComparison.Ordinal);
                    var drawer = ReadSource("Infrastructure/UI/DebugCenter/Pages/OverviewDebugCenterPageDrawer.cs");
                    Assert.Contains("RuntimeServiceHub.Shared.GetDiagnostics", drawer, StringComparison.Ordinal);
                    Assert.Contains("GameServiceHub.Shared.GetDiagnostics", drawer, StringComparison.Ordinal);
                }),
                ("overview refreshes each atomic diagnostics snapshot once per draw", () =>
                {
                    var drawer = ReadSource("Infrastructure/UI/DebugCenter/Pages/OverviewDebugCenterPageDrawer.cs");
                    Assert.DoesNotContain("_runtimeDiagnostics", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("_gameDiagnostics", drawer, StringComparison.Ordinal);
                    Assert.Equal(1, CountOccurrences(drawer, "RuntimeServiceHub.Shared.GetDiagnostics()"));
                    Assert.Equal(1, CountOccurrences(drawer, "GameServiceHub.Shared.GetDiagnostics()"));
                    Assert.Contains(
                        "DebugCenterOverviewModel model = BuildModel(selectedPawn);",
                        drawer,
                        StringComparison.Ordinal);
                }),
                ("lifecycle diagnostics remain a sibling overview surface", () =>
                {
                    var registry = ReadSource("Infrastructure/UI/DebugCenter/DebugCenterPageRegistry.cs");
                    Assert.Contains("\"overview\"", registry, StringComparison.Ordinal);
                    Assert.DoesNotContain("runtime_console", registry, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("lifecycle_console", registry, StringComparison.OrdinalIgnoreCase);
                }),
                ("debug actions capture services at invocation time", () =>
                {
                    var actions = ReadDebugActionSources();
                    Assert.Contains("RuntimeServiceHub.Shared.Capture", actions, StringComparison.Ordinal);
                    Assert.DoesNotContain("CurrentRuntime<", actions, StringComparison.Ordinal);
                    Assert.DoesNotContain("CurrentGame<", actions, StringComparison.Ordinal);
                    Assert.DoesNotContain("private static IContextBuilder", actions, StringComparison.Ordinal);
                    Assert.DoesNotContain("private static IAgentBus", actions, StringComparison.Ordinal);
                }),
                ("runtime operation scope keeps one coherent generation and fences publication", () =>
                {
                    var hub = new RuntimeServiceHub();
                    Publish(hub, 1);
                    GenerationFencedOperation<IOperationValue> operation =
                        GenerationFencedOperation<IOperationValue>.Capture(
                        hub,
                        LifecycleEventSources.DebugAction,
                        scope => scope.GetRequired<IOperationValue>());

                    Publish(hub, 2);

                    Assert.Equal(1, operation.State.Value);
                    Assert.False(operation.CanPublish());
                    Assert.False(operation.CanPublish());
                    Assert.Equal(1, hub.GetDiagnostics().StaleCompletionDiscardCount);
                }),
                ("new lifecycle labels are localized in both languages", () =>
                {
                    var drawer = ReadSource("Infrastructure/UI/DebugCenter/Pages/OverviewDebugCenterPageDrawer.cs");
                    Assert.DoesNotContain("RuntimeDiagnostics?.State.ToString()", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("GameDiagnostics?.State.ToString()", drawer, StringComparison.Ordinal);
                    Assert.Contains("LocalizeLifecycleState", drawer, StringComparison.Ordinal);
                    foreach (var language in new[] { "English", "ChineseSimplified" })
                    {
                        var keyed = ReadCoreFile($"Languages/{language}/Keyed/RimMind_Core.xml");
                        Assert.Contains("RimMind.UI.Hub.Lifecycle.RuntimeState", keyed, StringComparison.Ordinal);
                        Assert.Contains("RimMind.UI.Hub.Lifecycle.GameState", keyed, StringComparison.Ordinal);
                        Assert.Contains("RimMind.UI.Hub.Lifecycle.StaleDiscards", keyed, StringComparison.Ordinal);
                        foreach (var state in new[] { "Building", "Running", "Stopped", "Failed", "NeverPublished" })
                            Assert.Contains($"RimMind.UI.Lifecycle.{state}", keyed, StringComparison.Ordinal);
                    }
                }));
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void Publish(RuntimeServiceHub hub, int value)
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind<IOperationValue>(new OperationValue(value));
            builder.Require<IOperationValue>();
            var snapshot = builder.Build();
            hub.Publish(
                snapshot,
                new RuntimeLifetime(snapshot.RuntimeId, hub.IsCurrent, hub.RecordStaleCompletion));
        }

        private interface IOperationValue
        {
            int Value { get; }
        }

        private sealed class OperationValue : IOperationValue
        {
            public OperationValue(int value) => Value = value;

            public int Value { get; }
        }

        private static string ReadDebugActionSources() =>
            string.Join(Environment.NewLine, new[]
            {
                ReadSource("Infrastructure/UI/AICoreDebugActions.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Requests.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.ContextAndAgents.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Windows.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Autotests.cs"),
            });

        private static string ReadSource(string relativePath) =>
            ReadCoreFile("Source/" + relativePath);

        private static string ReadCoreFile(string relativePath) =>
            File.ReadAllText(Path.Combine(CoreRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string CoreRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core");
        }
    }
}
