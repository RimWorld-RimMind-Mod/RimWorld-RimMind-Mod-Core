using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class ToolSet
        {
            private static readonly RuntimeServiceRef<IToolRegistry> ToolRegistries =
                RuntimeServiceRef<IToolRegistry>.Required();
            private static readonly RuntimeServiceRef<IGameMechanismRegistry> MechanismRegistries =
                RuntimeServiceRef<IGameMechanismRegistry>.Required();
            private static readonly RuntimeServiceRef<IAIRequestTraceLog> TraceLogs =
                RuntimeServiceRef<IAIRequestTraceLog>.Optional();

            public static IToolRegistry Registry => ToolRegistries.Value;
            public static IGameMechanismRegistry Mechanisms => MechanismRegistries.Value;

            /// <summary>
            /// Projects registered tool definitions filtered by <paramref name="scope"/>, optionally compacting parameter schemas.
            /// </summary>
            public static IReadOnlyList<ToolDefinition> GetDefinitionsForScope(ToolScope scope, bool compactSchema = false)
                => Registry.GetDefinitionsForScope(scope, compactSchema);

            /// <summary>
            /// Canonical mapping from <see cref="AutonomyDomain"/> + <see cref="ToolKind"/> to <see cref="RiskLevel"/>.
            /// </summary>
            public static RiskLevel MapAutonomyDomainRisk(AutonomyDomain? domain, ToolKind kind = ToolKind.Mutation)
                => AutonomyDomainRiskMapper.MapToRisk(domain, kind);

            public static ToolPolicyDecision? ClassifyPolicy(string? toolId, ToolCallArgs? args = null)
            {
                try
                {
                    return Registry.ClassifyPolicy(toolId, args);
                }
                catch
                {
                    return null;
                }
            }

            public static ToolPolicyDecision? Preflight(string? toolId, ToolCallArgs? args = null)
                => ClassifyPolicy(toolId, args);

            public static void RecordToolCallTrace(
                string? requestId,
                string toolName,
                string? argumentsJson,
                bool succeeded,
                string? resultSummary)
            {
                if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(toolName))
                    return;

                try
                {
                    var traceLog = TraceLogs.Value;
                    if (traceLog == null)
                        return;

                    string toolCallId = string.IsNullOrWhiteSpace(argumentsJson)
                        ? Guid.NewGuid().ToString("N")
                        : $"{toolName}:{Math.Abs(argumentsJson!.GetHashCode()):X8}";

                    traceLog.AddToolCall(
                        requestId!,
                        toolCallId,
                        toolName,
                        succeeded,
                        succeeded ? null : resultSummary);
                }
                catch
                {
                    // Never fail caller if runtime or trace log is unavailable.
                }
            }
        }
    }

    public static class RimMindToolsApiExtensions
    {
        public static void RecordToolCallTrace(
            this IToolRegistry? _,
            string? requestId,
            string toolName,
            string? argumentsJson,
            bool succeeded,
            string? resultSummary)
            => RimMindAPI.ToolSet.RecordToolCallTrace(requestId, toolName, argumentsJson, succeeded, resultSummary);
    }
}
