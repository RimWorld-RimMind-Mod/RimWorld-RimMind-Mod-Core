using System;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Resolves the approval risk of an agent action intent from registered tool metadata
    /// (<see cref="ToolManifest"/>) instead of guessing from the intent text.
    /// <list type="number">
    /// <item>Intent names a registered tool: that tool's manifest.</item>
    /// <item>Intent is a mechanism intent (<c>pawn.job.force_rest</c>): the manifest of the mechanism tool for
    /// the write operation the executor will invoke (falls back to the mechanism's own operation risk), escalated
    /// by <see cref="IMechanismActionRiskProvider"/> when the mechanism declares per-action risk.</item>
    /// <item>Anything else: <see cref="UnknownRisk"/>.</item>
    /// </list>
    /// A manifest with <see cref="ToolManifest.RequiresApproval"/> resolves to <see cref="RiskLevel.Critical"/>,
    /// so only full autonomy runs it without a player decision.
    /// </summary>
    public sealed class ToolManifestRiskResolver
    {
        /// <summary>Risk used when no tool or mechanism metadata describes the intent.</summary>
        public const RiskLevel UnknownRisk = RiskLevel.Medium;

        private readonly Func<IToolRegistry?> _tools;
        private readonly Func<IGameMechanismRegistry?>? _mechanisms;
        private readonly Func<string, string>? _intentToToolId;

        /// <param name="tools">Tool registry accessor (evaluated per call so runtime swaps are honoured).</param>
        /// <param name="mechanisms">Optional mechanism registry accessor for mechanism intents.</param>
        /// <param name="intentToToolId">Optional intent alias mapping; must match the executor's own mapping.</param>
        public ToolManifestRiskResolver(
            Func<IToolRegistry?> tools,
            Func<IGameMechanismRegistry?>? mechanisms = null,
            Func<string, string>? intentToToolId = null)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _mechanisms = mechanisms;
            _intentToToolId = intentToToolId;
        }

        public ToolManifestRiskResolver(
            IToolRegistry tools,
            IGameMechanismRegistry? mechanisms = null,
            Func<string, string>? intentToToolId = null)
            : this(() => tools, mechanisms == null ? null : () => mechanisms, intentToToolId)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
        }

        /// <summary>
        /// Resolves the risk of <paramref name="actionIntent"/>. Empty intents execute nothing and resolve to Low.
        /// </summary>
        public RiskLevel Resolve(string? actionIntent)
        {
            if (string.IsNullOrWhiteSpace(actionIntent)) return RiskLevel.Low;
            var intent = actionIntent!.Trim();
            if (string.Equals(intent, "dialogue.free", StringComparison.OrdinalIgnoreCase)) return RiskLevel.Low;

            var tools = SafeGet(_tools);
            var toolId = MapIntent(intent);
            var direct = SafeFind(tools, toolId);
            if (direct != null)
                return FromManifest(direct.Definition?.Manifest);

            var mechanisms = _mechanisms == null ? null : SafeGet(_mechanisms);
            if (mechanisms == null) return UnknownRisk;

            var (mechanismId, action) = DecisionMapper.ParseActionIntent(intent);
            IGameMechanism? mechanism;
            try { mechanism = mechanisms.FindById(mechanismId); }
            catch (Exception) { return UnknownRisk; }
            if (mechanism == null) return UnknownRisk;

            var operation = MechanismWriteOperationSelector.Select(
                mechanism.SupportedOperations,
                DecisionMapper.InferOperationType(action));
            if (!operation.HasValue) return UnknownRisk;

            var mechanismTool = SafeFind(tools, MechanismToolId(mechanismId, operation.Value));
            var risk = mechanismTool != null
                ? FromManifest(mechanismTool.Definition?.Manifest)
                : FromMechanismRisk(mechanism.GetRiskForOperation(operation.Value));

            if (mechanism is IMechanismActionRiskProvider actionRisk)
            {
                var declared = actionRisk.GetRiskForAction(action);
                if (declared.HasValue)
                {
                    var declaredLevel = FromMechanismRisk(declared.Value);
                    if (declaredLevel > risk) risk = declaredLevel;
                }
            }

            return risk;
        }

        /// <summary>Effective approval risk of a manifest; <c>RequiresApproval</c> escalates to Critical.</summary>
        public static RiskLevel FromManifest(ToolManifest? manifest)
        {
            if (manifest == null) return UnknownRisk;
            return manifest.RequiresApproval ? RiskLevel.Critical : manifest.RiskLevel;
        }

        /// <summary>Manifest risk level declared for a mechanism risk (used by mechanism tool manifests).</summary>
        public static RiskLevel MapMechanismRisk(MechanismRisk risk) => risk switch
        {
            MechanismRisk.Safe => RiskLevel.Low,
            MechanismRisk.Moderate => RiskLevel.Medium,
            MechanismRisk.Dangerous => RiskLevel.High,
            _ => UnknownRisk
        };

        /// <summary>Whether a mechanism risk requires explicit approval (mirrors mechanism tool manifests).</summary>
        public static bool RequiresApproval(MechanismRisk risk) => risk >= MechanismRisk.Dangerous;

        /// <summary>Effective approval risk of a mechanism risk, equal to <see cref="FromManifest"/> of its tool manifest.</summary>
        public static RiskLevel FromMechanismRisk(MechanismRisk risk)
            => RequiresApproval(risk) ? RiskLevel.Critical : MapMechanismRisk(risk);

        /// <summary>Tool id under which a mechanism operation is registered (e.g. <c>pawn.job.set</c>).</summary>
        public static string MechanismToolId(string mechanismId, MechanismOperationType operation)
            => $"{mechanismId}.{operation.ToString().ToLowerInvariant()}";

        private string MapIntent(string intent)
        {
            if (_intentToToolId == null) return intent;
            try
            {
                var mapped = _intentToToolId(intent);
                return string.IsNullOrWhiteSpace(mapped) ? intent : mapped;
            }
            catch (Exception)
            {
                return intent;
            }
        }

        private static T? SafeGet<T>(Func<T?> accessor) where T : class
        {
            try { return accessor(); }
            catch (Exception) { return null; }
        }

        private static IToolHandler? SafeFind(IToolRegistry? tools, string toolId)
        {
            if (tools == null || string.IsNullOrEmpty(toolId)) return null;
            try { return tools.FindById(toolId); }
            catch (Exception) { return null; }
        }
    }
}
