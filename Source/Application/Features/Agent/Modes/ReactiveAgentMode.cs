using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent.Modes;

public sealed class ReactiveAgentMode : IAgentMode
{
    public AgentModeId ModeId => AgentModeId.Reactive;
        public string DisplayName => "Reactive";
        public string Description => "Responds to perception events only";
        public string Id => ModeId.Value;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

    public bool IsApplicable(IAgentInfo agent)
        => agent.State == AgentState.Active;

    public bool ShouldThink(IAgentInfo agent, IReadOnlyList<PerceptionBufferEntry> perceptions)
        => perceptions.Count > 0;

    public IThinkStrategy GetThinkStrategy() => new ReactiveThinkStrategy();

    private static readonly string[] ExcludedToolPrefixes = { "planning.", "dream.", "reflect.", "trait_evolution." };

    public IReadOnlyList<string> AllowedToolIds(IToolRegistry registry)
        => registry.GetAllDefinitions()
            .Where(d => !ExcludedToolPrefixes.Any(p => d.Id.StartsWith(p)))
            .Select(d => d.Id)
            .ToList();
}

internal sealed class ReactiveThinkStrategy : IThinkStrategy
{
    public string ScenarioId => ScenarioIds.Decision;

    public LlmRequestEnvelope BuildEnvelope(IAgentInfo agent,
        IReadOnlyList<PerceptionBufferEntry> perceptions,
        IReadOnlyList<ToolDefinition> availableTools)
    {
        var query = ThinkStrategyHelper.FormatPerceptions(perceptions);
        var domainTools = ThinkStrategyHelper.ConvertToDomainTools(availableTools);
        var examples = ThinkStrategyHelper.BuildDecisionExamples();
        return LlmRequestEnvelopeBuilder
            .ForScenario(ScenarioId)
            .WithModId("RimMind.Agent")
            .WithNpcId(agent.NpcId)
            .WithGameStateInfo(new GameStateInfo().AddSection("perceptions", query))
            .WithSchema("<Action>...</Action>")
            .WithTools(domainTools)
            .WithExamples(examples)
            .Build();
    }

    public Result<AgentDecision, RimMindError> ParseDecision(IAgentInfo agent, LlmResponse response,
        IReadOnlyList<ToolResult>? toolCallResults = null)
        => ThinkStrategyHelper.ParseDecisionCore(response, toolCallResults);
}
