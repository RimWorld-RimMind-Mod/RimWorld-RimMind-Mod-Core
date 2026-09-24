using System;
using System.IO;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class VerseRuntimeRebindingContract
    {
        [Fact]
        public void Verse_objects_rebind_by_runtime_generation()
        {
            ContractCaseRunner.Run(
                ("bus component uses atomic runtime binding", () =>
                {
                    var source = ReadSource("Infrastructure/Verse/AgentBusGameComponent.cs");
                    Assert.Contains("RuntimeBinding", source, StringComparison.Ordinal);
                    Assert.Contains("Refresh", source, StringComparison.Ordinal);
                    Assert.Contains("Dispose", source, StringComparison.Ordinal);
                }),
                ("tick components use generation aware references", () =>
                {
                    Assert.Contains("RuntimeServiceRef<ITickableRequestQueue>", ReadSource("Infrastructure/Verse/AIRequestQueueGameComponent.cs"), StringComparison.Ordinal);
                    Assert.Contains("RuntimeBinding", ReadSource("Infrastructure/Verse/FlywheelGameComponent.cs"), StringComparison.Ordinal);
                    Assert.Contains("RuntimeServiceRef<IFlywheelParameterStore>", ReadSource("Infrastructure/Verse/FlywheelParameterStoreGameComponent.cs"), StringComparison.Ordinal);
                }),
                ("job driver resolves bridge and bus from one scope", () =>
                {
                    var source = ReadSource("Infrastructure/Patches/JobDriver_RimMindAction.cs");
                    Assert.Contains("RuntimeServiceHub.Shared.Capture", source, StringComparison.Ordinal);
                    Assert.Contains("IAgentActionBridgeAccessor", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("static IAgentActionBridge", source, StringComparison.Ordinal);
                }),
                ("npc consumers retain accessors and resolve current per operation", () =>
                {
                    AssertNpcAccessor("Application/Features/Pipeline/Unified/NpcEnrichMiddleware.cs");
                    AssertNpcAccessor("Presentation/Context/ContextOrchestrator.cs");
                    AssertNpcAccessor("Presentation/Agent/GameContextBuilder.cs");
                    Assert.DoesNotContain(
                        "npcManagers.Current",
                        ReadSource("Presentation/Runtime/Composition/ContextComposition.cs"),
                        StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        "npcManagers.Current",
                        ReadSource("Presentation/Runtime/Composition/AgentComposition.cs"),
                        StringComparison.Ordinal);
                }),
                ("pawn extraction has no recomposition-sensitive static log state", () =>
                {
                    var source = ReadSource("Presentation/Agent/PawnDataExtractor.cs");
                    Assert.DoesNotContain("static ILogSink", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("void Initialize(", source, StringComparison.Ordinal);
                    Assert.Contains("Extract(Pawn pawn, ILogSink? logSink)", source, StringComparison.Ordinal);
                }));
        }

        private static void AssertNpcAccessor(string relativePath)
        {
            var source = ReadSource(relativePath);
            Assert.Contains("INpcManagerAccessor", source, StringComparison.Ordinal);
            Assert.Contains(".Current", source, StringComparison.Ordinal);
            Assert.DoesNotContain("readonly INpcManager? _npcManager", source, StringComparison.Ordinal);
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }
    }
}
