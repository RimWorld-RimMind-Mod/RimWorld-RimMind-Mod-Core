using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Planning;
using RimMind.Application.Common.Interfaces.Agent.Reflection;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent.Modes;

public sealed class ProactiveAgentMode : IAgentMode, IProactiveExtensions
{
    private readonly ITickProvider _tickProvider;
    private readonly IReflectionStrategy? _reflectionStrategy;
    private readonly IDailyPlanner? _dailyPlanner;
    private readonly IPsychologyWatcher? _psychologyWatcher;
    private readonly ISocialEventOrganizer? _socialEventOrganizer;
    private readonly ITraitEvolutionEngine? _traitEvolutionEngine;

    public ProactiveAgentMode(ITickProvider tickProvider,
        IReflectionStrategy? reflectionStrategy = null,
        IDailyPlanner? dailyPlanner = null,
        IPsychologyWatcher? psychologyWatcher = null,
        ISocialEventOrganizer? socialEventOrganizer = null,
        ITraitEvolutionEngine? traitEvolutionEngine = null)
    {
        _tickProvider = tickProvider;
        _reflectionStrategy = reflectionStrategy;
        _dailyPlanner = dailyPlanner;
        _psychologyWatcher = psychologyWatcher;
        _socialEventOrganizer = socialEventOrganizer;
        _traitEvolutionEngine = traitEvolutionEngine;
    }

    public AgentModeId ModeId => AgentModeId.Proactive;
        public string DisplayName => "Proactive";
        public string Description => "Periodically initiates decisions even without perception triggers";
        public string Id => ModeId.Value;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

    private const int ProactiveTickInterval = RimMindDefaults.ProactiveTickInterval;

    public bool IsApplicable(IAgentInfo agent) => agent.State == AgentState.Active;

    public bool ShouldThink(IAgentInfo agent, IReadOnlyList<PerceptionBufferEntry> perceptions)
    {
        if (perceptions.Count > 0) return true;

        var lastThinkTick = agent.LastThinkTick;
        if (lastThinkTick == null) return true;

        var ticksSinceLastThink = _tickProvider.TicksGame - lastThinkTick.Value;
        if (ticksSinceLastThink >= ProactiveTickInterval) return true;

        if (_reflectionStrategy != null && _reflectionStrategy.ShouldReflect(agent)) return true;
        if (_dailyPlanner != null && _dailyPlanner.ShouldPlan(agent)) return true;
        if (_psychologyWatcher?.HasUrgentEvent(agent.NpcId) == true) return true;

        // Social event trigger
        if (_socialEventOrganizer?.ShouldOrganize(agent) == true) return true;

        // Trait evolution trigger
        if (_traitEvolutionEngine?.ShouldEvolve(agent) == true) return true;

        return false;
    }

    public IReflectionStrategy? ReflectionStrategy => _reflectionStrategy;
    public IDailyPlanner? DailyPlanner => _dailyPlanner;
    public IPsychologyWatcher? PsychologyWatcher => _psychologyWatcher;
    public ISocialEventOrganizer? SocialEventOrganizer => _socialEventOrganizer;
    public ITraitEvolutionEngine? TraitEvolutionEngine => _traitEvolutionEngine;

    public IThinkStrategy GetThinkStrategy() => new ProactiveThinkStrategy();

    public IReadOnlyList<string> AllowedToolIds(IToolRegistry registry)
        => registry.GetAllDefinitions().Select(d => d.Id).ToList();
}

internal sealed class ProactiveThinkStrategy : IThinkStrategy
{
    public string ScenarioId => ScenarioIds.Decision;

    public LlmRequestEnvelope BuildEnvelope(IAgentInfo agent,
        IReadOnlyList<PerceptionBufferEntry> perceptions,
        IReadOnlyList<ToolDefinition> availableTools)
    {
        var query = perceptions.Count > 0
            ? ThinkStrategyHelper.FormatPerceptions(perceptions)
            : $"Periodic self-evaluation. Current state: {SerializeAgentState(agent)}";

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

    private static string SerializeAgentState(IAgentInfo agent)
    {
        return $"Pawn={agent.Label}, State={agent.State}, Goals={agent.GoalCount}";
    }
}
