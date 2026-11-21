using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Generic base class for composite ToolCalls that orchestrate atomic tools or game mutations.
    /// Provides single-pass JSON argument parsing, autonomy risk mapping, colonist target liveness guards,
    /// and atomic sub-tool execution helpers.
    /// </summary>
    public abstract class CompositeToolCallBase : ICompositeToolCall, IToolPolicyClassifier
    {
        private ToolDefinition? _cachedDefinition;

        public abstract string Id { get; }

        public virtual string OwnerModId => RimMindOwnerConsts.CoreModId;

        protected virtual ToolKind ToolKind => ToolKind.Mutation;

        protected virtual AutonomyDomain? DefaultAutonomyDomain => null;

        protected virtual RiskLevel DefaultRiskLevel => AutonomyDomainRiskMapper.MapToRisk(DefaultAutonomyDomain, ToolKind);

        protected virtual bool DefaultRequiresApproval => false;

        protected virtual ToolScope? ToolScopes => null;

        protected virtual IReadOnlyList<string> ToolAliases => Array.Empty<string>();

        protected virtual bool IsSelfTargetedTool => false;

        protected virtual string? DisplayName => null;

        protected virtual IReadOnlyList<string> SummaryArgKeys => Array.Empty<string>();

        public virtual ToolManifest Manifest => new ToolManifest
        {
            OwnerModId = OwnerModId,
            Kind = ToolKind,
            DefaultDomain = DefaultAutonomyDomain,
            AutonomyDomainId = DefaultAutonomyDomain?.ToString(),
            RiskLevel = DefaultRiskLevel,
            RequiresApproval = DefaultRequiresApproval,
            Aliases = ToolAliases,
            IsSelfTargeted = IsSelfTargetedTool,
            DisplayName = DisplayName,
            SummaryArgKeys = SummaryArgKeys,
            Scopes = ToolScopes
        };

        public virtual ToolDefinition Definition => _cachedDefinition ??= BuildDefinition() with
        {
            Manifest = Manifest
        };

        protected virtual ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Manifest = Manifest
        };

        public abstract IReadOnlyList<string> RequiredToolIds { get; }

        public abstract Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct);

        protected virtual AutonomyDomain? ResolveAutonomyDomain(JObject? root, ToolCallArgs args)
            => DefaultAutonomyDomain;

        public virtual ToolPolicyDecision Classify(ToolCallArgs args)
        {
            var effectiveArgs = args ?? new ToolCallArgs
            {
                ToolName = Id,
                ArgumentsJson = "{}"
            };
            var root = TryParseArgumentsObject(effectiveArgs.ArgumentsJson);
            var domain = ResolveAutonomyDomain(root, effectiveArgs);
            return new ToolPolicyDecision
            {
                Domain = domain,
                DomainId = domain?.ToString() ?? Manifest.AutonomyDomainId,
                Risk = AutonomyDomainRiskMapper.MapToRisk(domain, ToolKind),
                RequiresApproval = Manifest.RequiresApproval
            };
        }

        protected static JObject? TryParseArgumentsObject(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;
            try
            {
                return JToken.Parse(argumentsJson!) as JObject;
            }
            catch
            {
                return null;
            }
        }

        protected virtual IToolHandler? FindTool(string toolId) => RimMindAPI.Tools.FindById(toolId);

        protected virtual bool AreRequiredToolsRegistered()
        {
            foreach (var toolId in RequiredToolIds)
            {
                if (FindTool(toolId) == null)
                    return false;
            }
            return true;
        }

        protected async Task<ToolResult> ExecuteAtomicAsync(
            string toolId,
            string argumentsJson,
            ToolCallArgs parentArgs,
            CancellationToken ct)
        {
            var childCallId = $"{parentArgs.ToolCallId}:{toolId}";
            var handler = FindTool(toolId);
            if (handler == null)
            {
                return ToolResult.Fail($"Required tool not registered: {toolId}", childCallId, toolId);
            }

            var childArgs = new ToolCallArgs
            {
                ToolCallId = childCallId,
                ToolName = toolId,
                ArgumentsJson = argumentsJson,
                PawnId = parentArgs.PawnId,
                NpcId = parentArgs.NpcId,
                TraceId = parentArgs.TraceId,
                Ct = ct,
                IsPlayerApproved = parentArgs.IsPlayerApproved
            };

            var result = await handler.ExecuteAsync(childArgs, ct);
            return result.IsOk
                ? result.Value with { ToolCallId = childCallId, ToolName = toolId }
                : ToolResult.Fail($"[{result.Error.Code}] {result.Error.Message}", childCallId, toolId);
        }

        protected static bool TryParsePawnIdString(string? raw, out int parsedId)
            => PawnIdParser.TryParse(raw, out parsedId);

        /// <summary>
        /// Checks whether trusted host code approved this call.
        /// Approval is read only from <see cref="ToolCallArgs.IsPlayerApproved"/>.
        /// </summary>
        public static bool IsPreApproved(ToolCallArgs? args) => args != null && args.IsPlayerApproved;

        /// <summary>
        /// Fails fast with a structured <see cref="RimMindError"/> if invoked off the Unity main thread
        /// before touching Verse / RimWorld map or pawn state.
        /// </summary>
        protected Result<Unit, RimMindError> EnsureMainThread(ToolCallArgs? args = null)
        {
            if (!Verse.UnityData.IsInMainThread)
            {
                return Result<Unit, RimMindError>.Err(
                    new RimMindError(
                        RimMindErrorCode.ToolExecutionFailed,
                        $"Tool '{Id}' cannot execute off the Unity main thread")
                    {
                        TraceId = args?.TraceId
                    });
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Verifies that <paramref name="pawnId"/> is an active spawned player colonist and passes autonomy checks.
        /// </summary>
        protected virtual Result<Unit, RimMindError> GuardColonistTarget(
            int pawnId,
            AutonomyDomain domain,
            ToolCallArgs args,
            string argumentKey = "pawn_id")
        {
            var threadGuard = EnsureMainThread(args);
            if (threadGuard.IsErr)
                return threadGuard;

            var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                return Result<Unit, RimMindError>.Err(
                    new RimMindError(
                        RimMindErrorCode.MechanismInvalidAction,
                        $"{argumentKey} {pawnId} is not an active spawned colonist")
                    {
                        TraceId = args?.TraceId
                    });
            }

            return CheckPermission(pawn, domain, args);
        }

        /// <summary>
        /// Checks autonomy permissions against the colonist and autonomy domain, honoring player pre-approvals.
        /// Submodules may override to attach custom notifications or approval-queue routing.
        /// </summary>
        protected virtual Result<Unit, RimMindError> CheckPermission(
            Verse.Pawn? pawn,
            AutonomyDomain domain,
            ToolCallArgs? args)
        {
            if (IsPreApproved(args))
                return Result<Unit, RimMindError>.Ok(Unit.Value);

            var level = RimMindAPI.Autonomy.GetLevel(pawn, domain);
            if (level == AutonomyLevel.Disabled)
            {
                return Result<Unit, RimMindError>.Err(
                    new RimMindError(
                        RimMindErrorCode.MechanismInvalidAction,
                        $"Action '{Id}' in domain '{domain}' is disabled by player autonomy policy")
                    {
                        TraceId = args?.TraceId
                    });
            }

            if (level == AutonomyLevel.RequireApproval)
            {
                return Result<Unit, RimMindError>.Err(
                    new RimMindError(
                        RimMindErrorCode.ToolPolicyDenied,
                        $"Action '{Id}' in domain '{domain}' requires player approval")
                    {
                        TraceId = args?.TraceId
                    });
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Reads a typed argument from a pre-parsed <see cref="JObject"/> (zero re-parsing).
        /// </summary>
        protected static bool TryGetArgument<T>(JObject? root, string key, out T value)
        {
            value = default!;
            if (root == null || string.IsNullOrEmpty(key))
                return false;

            try
            {
                var token = root[key];
                if (token == null || token.Type == JTokenType.Null)
                    return false;

                if (typeof(T) == typeof(int) && token.Type == JTokenType.String)
                {
                    if (PawnIdParser.TryParse(token.ToString(), out int parsedInt))
                    {
                        value = (T)(object)parsedInt;
                        return true;
                    }
                    return false;
                }

                var converted = token.Value<T>();
                if (converted == null)
                    return false;

                value = converted;
                return true;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidCastException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads a typed argument from raw JSON arguments. Prefer the <see cref="JObject"/> overload when reading multiple keys.
        /// </summary>
        protected static bool TryGetArgument<T>(string? argumentsJson, string key, out T value)
        {
            var root = TryParseArgumentsObject(argumentsJson);
            return TryGetArgument(root, key, out value);
        }

        protected static Result<T, RimMindError> TryGetArgumentOrError<T>(
            JObject? root, string key, string? traceId)
        {
            if (TryGetArgument(root, key, out T value))
                return Result<T, RimMindError>.Ok(value);

            return Result<T, RimMindError>.Err(
                new RimMindError(RimMindErrorCode.MechanismInvalidAction, $"Missing or invalid {key}")
                {
                    TraceId = traceId
                });
        }

        protected static Result<T, RimMindError> TryGetArgumentOrError<T>(
            string? argumentsJson, string key, string? traceId)
        {
            if (TryGetArgument(argumentsJson, key, out T value))
                return Result<T, RimMindError>.Ok(value);

            return Result<T, RimMindError>.Err(
                new RimMindError(RimMindErrorCode.MechanismInvalidAction, $"Missing or invalid {key}")
                {
                    TraceId = traceId
                });
        }

        protected static Result<int, RimMindError> TryGetPawnIdOrError(
            JObject? root, ToolCallArgs args, string key = "pawn_id")
        {
            int contextFallbackId = (args.PawnId.HasValue && args.PawnId.Value > 0)
                ? args.PawnId.Value
                : (PawnIdParser.TryParse(args.NpcId, out int parsedNpcId) && parsedNpcId > 0 ? parsedNpcId : 0);

            if (TryGetArgument<int>(root, key, out int id) && id > 0)
                return Result<int, RimMindError>.Ok(id);

            if (key != "pawn_id" && TryGetArgument<int>(root, "pawn_id", out int fallbackId) && fallbackId > 0)
                return Result<int, RimMindError>.Ok(fallbackId);

            if (contextFallbackId > 0)
                return Result<int, RimMindError>.Ok(contextFallbackId);

            return Result<int, RimMindError>.Err(
                new RimMindError(RimMindErrorCode.MechanismInvalidAction, $"Missing or invalid {key}")
                {
                    TraceId = args.TraceId
                });
        }

        protected static Result<int, RimMindError> TryGetPawnIdOrError(
            ToolCallArgs args, string key = "pawn_id")
            => TryGetPawnIdOrError(TryParseArgumentsObject(args?.ArgumentsJson), args!, key);

        protected static string BuildArgumentsJson(params (string Key, object? Value)[] pairs)
        {
            var obj = new JObject();
            foreach (var (key, val) in pairs)
            {
                obj[key] = val == null ? JValue.CreateNull() : JToken.FromObject(val);
            }
            return obj.ToString(Formatting.None);
        }

        protected static string BuildStepSummary(params (string Name, ToolResult Result)[] steps)
        {
            var obj = new JObject();
            foreach (var (name, result) in steps)
            {
                obj[name] = new JObject
                {
                    ["ok"] = !result.IsError,
                    ["content"] = result.Content
                };
            }
            return obj.ToString(Formatting.None);
        }
    }
}
