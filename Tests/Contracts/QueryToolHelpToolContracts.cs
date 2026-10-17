using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class QueryToolHelpToolContracts
    {
        [Fact]
        public void Query_tool_help_provides_full_introspection_and_registration()
        {
            ContractCaseRunner.Run(
                ("specific tool query returns parameters and schema", () =>
                {
                    var registry = new ToolRegistry();
                    var helpTool = new QueryToolHelpTool(registry);
                    registry.Register(helpTool);

                    var dummyTool = new DummyToolHandler("test_action", "Executes test action", "{\"type\":\"object\"}");
                    registry.Register(dummyTool);

                    var task = helpTool.ExecuteAsync(new ToolCallArgs
                    {
                        ToolCallId = "call-1",
                        ToolName = "query_tool_help",
                        ArgumentsJson = "{\"tool_name\":\"test_action\"}"
                    }, CancellationToken.None);

                    task.Wait();
                    var result = task.Result;

                    Assert.True(result.IsOk);
                    Assert.False(result.Value.IsError);
                    var json = JObject.Parse(result.Value.Content);
                    Assert.Equal("ok", json["status"]?.Value<string>());
                    Assert.Equal("test_action", json["tool_id"]?.Value<string>());
                    Assert.Equal("Executes test action", json["description"]?.Value<string>());
                }),
                ("empty query returns catalog of all registered tools", () =>
                {
                    var registry = new ToolRegistry();
                    var helpTool = new QueryToolHelpTool(registry);
                    registry.Register(helpTool);
                    registry.Register(new DummyToolHandler("tool_a", "Description A", "{}"));
                    registry.Register(new DummyToolHandler("tool_b", "Description B", "{}"));

                    var task = helpTool.ExecuteAsync(new ToolCallArgs
                    {
                        ToolCallId = "call-2",
                        ToolName = "query_tool_help",
                        ArgumentsJson = "{}"
                    }, CancellationToken.None);

                    task.Wait();
                    var result = task.Result;

                    Assert.True(result.IsOk);
                    var json = JObject.Parse(result.Value.Content);
                    Assert.Equal("ok", json["status"]?.Value<string>());
                    Assert.Equal(3, json["total_tools"]?.Value<int>());
                }),
                ("unknown tool query returns informative not_found error", () =>
                {
                    var registry = new ToolRegistry();
                    var helpTool = new QueryToolHelpTool(registry);
                    registry.Register(helpTool);

                    var task = helpTool.ExecuteAsync(new ToolCallArgs
                    {
                        ToolCallId = "call-3",
                        ToolName = "query_tool_help",
                        ArgumentsJson = "{\"tool_name\":\"nonexistent_tool\"}"
                    }, CancellationToken.None);

                    task.Wait();
                    var result = task.Result;

                    Assert.True(result.IsOk);
                    Assert.True(result.Value.IsError);
                    var json = JObject.Parse(result.Value.Content);
                    Assert.Equal("not_found", json["status"]?.Value<string>());
                }),
                ("dependency injection wires QueryToolHelpTool into ToolRegistry", () =>
                {
                    var source = ReadSource("Application/DependencyInjection.cs");
                    Assert.Contains("toolRegistry.Register(new QueryToolHelpTool(toolRegistry));", source, StringComparison.Ordinal);
                }));
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Source"));
        }

        private sealed class DummyToolHandler : IToolHandler
        {
            public DummyToolHandler(string id, string desc, string schema)
            {
                Definition = new ToolDefinition
                {
                    Id = id,
                    Description = desc,
                    ParametersSchema = schema,
                    Category = "test"
                };
            }

            public string Id => Definition.Id;
            public string OwnerModId => "TestMod";
            public ToolDefinition Definition { get; }

            public Task<RimMind.Domain.ValueObjects.Result<ToolResult, RimMind.Domain.ValueObjects.RimMindError>> ExecuteAsync(
                ToolCallArgs args, CancellationToken ct)
            {
                return Task.FromResult(RimMind.Domain.ValueObjects.Result<ToolResult, RimMind.Domain.ValueObjects.RimMindError>.Ok(
                    ToolResult.Ok("dummy", args.ToolCallId, Id)));
            }
        }
    }
}
