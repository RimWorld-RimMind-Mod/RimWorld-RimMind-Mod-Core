using System;
using System.IO;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class ToolCallDepthContract
    {
        [Fact]
        public void Configured_tool_call_depth_controls_the_agent_loop()
        {
            ContractCaseRunner.Run(
                ("agent loop enforces its configured depth", () =>
                {
                    var loop = new AgenticLoopService(maxDepth: 2);
                    var requestMoreTools = new AgentDecision(WantsMoreToolCalls: true);

                    Assert.Equal(2, loop.MaxDepth);
                    Assert.True(loop.ShouldContinue(requestMoreTools, currentDepth: 0));
                    Assert.False(loop.ShouldContinue(requestMoreTools, currentDepth: 1));
                }),
                ("pawn thinker wires tick settings into the real loop", () =>
                {
                    var thinker = ReadSource("Presentation/Agent/PawnThinker.cs");
                    Assert.Contains(
                        "new AgenticLoopService(tickSettings.MaxToolCallDepth)",
                        thinker,
                        StringComparison.Ordinal);
                }),
                ("single-dispatch middleware and runtime expose no fake depth state", () =>
                {
                    var middleware = ReadSource("Application/Features/Pipeline/Unified/ToolCallDispatchMiddleware.cs");
                    var runtime = ReadSource("Presentation/Runtime/RimMindRuntime.cs");

                    Assert.DoesNotContain("_maxDepth", middleware, StringComparison.Ordinal);
                    Assert.DoesNotContain("maxDepth", middleware, StringComparison.Ordinal);
                    Assert.DoesNotContain("MaxToolCallDepth", runtime, StringComparison.Ordinal);
                }),
                ("settings use the shared default and preserve the scribe key", () =>
                {
                    var settings = ReadSource("Presentation/Settings/RimMindCoreSettings.cs");
                    Assert.Contains(
                        "maxToolCallDepth = RimMindDefaults.DefaultMaxToolCallDepth",
                        settings,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "ref maxToolCallDepth, \"maxToolCallDepth\", RimMindDefaults.DefaultMaxToolCallDepth",
                        settings,
                        StringComparison.Ordinal);
                }));
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
