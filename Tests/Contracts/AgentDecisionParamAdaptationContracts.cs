using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Registry;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Agent;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Infrastructure.Mechanisms.Pawn.Job;
using RimMind.Infrastructure.Mechanisms.Pawn.Work;
using Xunit;

namespace RimMind.Tests.Contracts
{
    /// <summary>
    /// Regression: LLM decisions with plain-text Param (e.g. "param":"Construction") were silently
    /// replaced with {} so pawn.work.set failed with "Invalid def name ''".
    /// Verifies Core's pluggable IToolArgumentAdapter implementations via MechanismActionExecutor.
    /// </summary>
    public sealed class AgentDecisionParamAdaptationContracts
    {
        private static IToolArgumentAdapter[] CoreAdapters() => new IToolArgumentAdapter[]
        {
            new WorkMechanismArgumentAdapter(),
            new JobMechanismArgumentAdapter()
        };

        [Fact]
        public void PlainTextParam_IsForwardedAsWorkTarget_ForWorkSetTool()
        {
            var tools = new ToolRegistry();
            var workTool = new RecordingToolHandler("pawn.work.set");
            tools.Register(workTool);
            var executor = new MechanismActionExecutor(new GameMechanismRegistry(), tools, CoreAdapters);

            var result = executor.ExecuteDecision(
                new AgentDecision(ActionIntent: "pawn.work.set", Reason: "build shelter", Param: "Construction"),
                pawnId: 42);

            Assert.True(result.IsOk);
            var args = JObject.Parse(workTool.LastArgumentsJson!);
            Assert.Equal("Construction", args.Value<string>("def_name"));
            Assert.Equal("Construction", args.Value<string>("work_type"));
            Assert.Equal(1, args.Value<int>("priority"));
            Assert.Equal(42, args.Value<int>("pawn_id"));
        }

        [Fact]
        public void PlainTextParam_DoesNotForceAssignWork_ForJobSetTool()
        {
            var tools = new ToolRegistry();
            var jobTool = new RecordingToolHandler("pawn.job.set");
            tools.Register(jobTool);
            var executor = new MechanismActionExecutor(new GameMechanismRegistry(), tools, CoreAdapters);

            var result = executor.ExecuteDecision(
                new AgentDecision(ActionIntent: "pawn.job.set", Reason: "starving", Param: "eat a meal"),
                pawnId: 7);

            Assert.True(result.IsOk);
            var args = JObject.Parse(jobTool.LastArgumentsJson!);
            Assert.Equal("eat_food", args.Value<string>("action"));
            Assert.Null(args["def_name"]);
        }

        [Fact]
        public void ArgumentlessJobSet_IsNotSilentlyTurnedIntoForceRest()
        {
            // Live playthrough: 65 argument-less pawn_job_set calls became force_rest and kept
            // colonists lying down instead of eating, tending or working.
            var tools = new ToolRegistry();
            var jobTool = new RecordingToolHandler("pawn_job_set");
            tools.Register(jobTool);
            var executor = new MechanismActionExecutor(new GameMechanismRegistry(), tools, CoreAdapters);

            executor.ExecuteDecision(new AgentDecision(ActionIntent: "pawn_job_set", Reason: "", Param: "{}"), pawnId: 5);

            var args = JObject.Parse(jobTool.LastArgumentsJson!);
            Assert.NotEqual("force_rest", args.Value<string>("action"));
        }

        private sealed class RecordingToolHandler : IToolHandler
        {
            public RecordingToolHandler(string id)
            {
                Id = id;
                Definition = new ToolDefinition { Id = id, Description = id, ParametersSchema = "{}" };
            }

            public string Id { get; }
            public string OwnerModId => "test";
            public ToolDefinition Definition { get; }
            public string? LastArgumentsJson { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                LastArgumentsJson = args.ArgumentsJson;
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult { ToolCallId = args.ToolCallId, Content = "ok" }));
            }
        }
    }
}
