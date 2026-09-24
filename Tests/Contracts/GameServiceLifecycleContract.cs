using System;
using System.IO;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class GameServiceLifecycleContract
    {
        [Fact]
        public void Game_services_publish_once_per_game_and_stop_cleanly()
        {
            ContractCaseRunner.Run(
                ("new and loaded games publish complete game services", () =>
                {
                    var source = ReadSource("Presentation/Runtime/RimMindRuntimeGameComponent.cs");
                    Assert.Contains("GameServiceBuilder", source, StringComparison.Ordinal);
                    Assert.Contains("Bind<INpcManager>", source, StringComparison.Ordinal);
                    Assert.Contains("Bind<IAIDebugLog>", source, StringComparison.Ordinal);
                    Assert.Contains("GameServiceHub.Shared.Publish", source, StringComparison.Ordinal);
                    Assert.Contains("GameServiceHub.Shared.Stop", source, StringComparison.Ordinal);
                }),
                ("verse game services never self-register", () =>
                {
                    var npcManager = ReadSource("Infrastructure/Verse/NpcManager.cs");
                    var debugLog = ReadSource("Infrastructure/Verse/AIDebugLog.cs");
                    Assert.DoesNotContain("RimMindServiceLocator", npcManager, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindServiceLocator", debugLog, StringComparison.Ordinal);
                    Assert.DoesNotContain("AttachGameService", npcManager, StringComparison.Ordinal);
                    Assert.DoesNotContain("AttachGameService", debugLog, StringComparison.Ordinal);
                    var owner = ReadSource("Presentation/Runtime/RimMindRuntimeGameComponent.cs");
                    Assert.Contains("ResolveGameComponent<INpcManager>(_game)", owner, StringComparison.Ordinal);
                    Assert.Contains("ResolveGameComponent<IAIDebugLog>(_game)", owner, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMind.Infrastructure.Verse", owner, StringComparison.Ordinal);
                    Assert.DoesNotContain("static INpcManager", owner, StringComparison.Ordinal);
                    Assert.DoesNotContain("static IAIDebugLog", owner, StringComparison.Ordinal);
                }),
                ("runtime consumes narrow game accessors", () =>
                {
                    var source = ReadSource("Presentation/Runtime/Services/GameServiceAccessors.cs");
                    Assert.Contains("GameServiceRef<INpcManager>.Optional", source, StringComparison.Ordinal);
                    Assert.Contains("GameServiceRef<IAIDebugLog>.Optional", source, StringComparison.Ordinal);
                }),
                ("returning to the main menu stops only the game lifecycle", () =>
                {
                    Assert.True(
                        File.Exists(SourcePath("Infrastructure/Patches/GenScene_GameLifecyclePatch.cs")),
                        "The return-to-main-menu lifecycle patch is missing.");
                    var patch = ReadSource("Infrastructure/Patches/GenScene_GameLifecyclePatch.cs");
                    Assert.Contains(
                        "HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))",
                        patch,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "RimMindRuntimeGameComponent.StopGameServices()",
                        patch,
                        StringComparison.Ordinal);

                    var owner = ReadSource("Presentation/Runtime/RimMindRuntimeGameComponent.cs");
                    Assert.Contains("GameServiceHub.Shared.Stop()", owner, StringComparison.Ordinal);

                    var host = ReadSource("Presentation/Runtime/RimMindRuntimeHost.cs");
                    var recomposeStart = host.IndexOf("public static bool TryRecompose(", StringComparison.Ordinal);
                    var shutdownStart = host.IndexOf("public static void Shutdown()", recomposeStart, StringComparison.Ordinal);
                    Assert.True(recomposeStart >= 0 && shutdownStart > recomposeStart);
                    Assert.DoesNotContain(
                        "StopGameServices",
                        host.Substring(recomposeStart, shutdownStart - recomposeStart),
                        StringComparison.Ordinal);
                }));
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(SourcePath(relativePath));

        private static string SourcePath(string relativePath) =>
            Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }
    }
}
