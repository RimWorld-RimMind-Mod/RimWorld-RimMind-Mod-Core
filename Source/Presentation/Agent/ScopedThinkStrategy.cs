using System.Collections.Generic;
using System.Text;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Domain.Agent.Modes;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Agent
{
    public sealed class ScopedThinkStrategy : IThinkStrategy
    {
        private readonly string _scopeType;

        public string ScenarioId => _scopeType switch
        {
            "Storyteller" => ScenarioIds.Storyteller,
            _ => ScenarioIds.Decision
        };

        public ScopedThinkStrategy(string scopeType)
        {
            _scopeType = scopeType ?? "unknown";
        }

        public LlmRequestEnvelope BuildEnvelope(
            IAgentInfo agent,
            IReadOnlyList<PerceptionBufferEntry> perceptions,
            IReadOnlyList<ToolDefinition> availableTools)
        {
            var query = BuildScopeContext(perceptions);
            var domainTools = ThinkStrategyHelper.ConvertToDomainTools(availableTools);
            var examples = ThinkStrategyHelper.BuildDecisionExamples();

            return LlmRequestEnvelopeBuilder
                .ForScenario(ScenarioId)
                .WithModId("RimMind.ScopedAgent")
                .WithNpcId(agent.NpcId)
                .WithGameStateInfo(new GameStateInfo()
                    .AddSection("scope_type", _scopeType)
                    .AddSection("perceptions", query))
                .WithSchema("<Action>...</Action>")
                .WithTools(domainTools)
                .WithExamples(examples)
                .Build();
        }

        public Result<AgentDecision, RimMindError> ParseDecision(
            IAgentInfo agent,
            LlmResponse response,
            IReadOnlyList<ToolResult>? toolCallResults = null)
            => ThinkStrategyHelper.ParseDecisionCore(response, toolCallResults);

        private string BuildScopeContext(IReadOnlyList<PerceptionBufferEntry> perceptions)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<scope type=\"{_scopeType}\">");

            if (perceptions.Count > 0)
            {
                sb.Append(ThinkStrategyHelper.FormatPerceptions(perceptions));
            }

            sb.AppendLine("</scope>");
            return sb.ToString();
        }
    }
}
