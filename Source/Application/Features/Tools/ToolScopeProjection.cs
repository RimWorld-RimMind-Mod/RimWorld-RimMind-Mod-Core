using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Resolves effective <see cref="ToolScope"/> flags from declarative <see cref="ToolManifest"/>
    /// metadata and produces compact JSON schema projections to eliminate prompt token bloat.
    /// </summary>
    public static class ToolScopeProjection
    {
        public const ToolScope ColonistAgentDefaultScope =
            ToolScope.ColonistSelf | ToolScope.ColonyBuild | ToolScope.ColonyManage | ToolScope.Social;

        public const ToolScope AdvisorDefaultScope =
            ToolScope.ColonistSelf | ToolScope.ColonyBuild | ToolScope.ColonyManage | ToolScope.Social;

        private const int MaxCompactDescriptionChars = 120;
        private const int MaxCompactParamDescChars = 72;

        public static ToolScope ResolveScopes(ToolDefinition? definition)
        {
            if (definition == null)
                return ToolScope.None;

            var manifest = definition.Manifest;
            if (manifest?.Scopes is ToolScope declared && declared != ToolScope.None)
                return declared;

            string id = (definition.Id ?? string.Empty).Trim();
            string category = (definition.Category ?? string.Empty).Trim();

            // Meta / listing / help queries
            if (id.EndsWith(".list", StringComparison.OrdinalIgnoreCase) ||
                id.EndsWith("_list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "query_tool_help", StringComparison.OrdinalIgnoreCase))
            {
                return ToolScope.MetaQuery;
            }

            // World-level admin mechanisms/tools
            if (string.Equals(category, "world", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("world.", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("world_", StringComparison.OrdinalIgnoreCase))
            {
                return ToolScope.WorldAdmin;
            }

            // Mechanism read queries (already represented in L1/L2/L3 context)
            if (manifest?.Kind == ToolKind.Query &&
                (id.EndsWith(".query", StringComparison.OrdinalIgnoreCase) ||
                 id.EndsWith("_query", StringComparison.OrdinalIgnoreCase)))
            {
                return ToolScope.MetaQuery;
            }

            ToolScope inferred = ToolScope.None;

            AutonomyDomain? resolvedDomain = manifest?.DefaultDomain;
            if (!resolvedDomain.HasValue
                && !string.IsNullOrWhiteSpace(manifest?.AutonomyDomainId)
                && Enum.TryParse<AutonomyDomain>(manifest!.AutonomyDomainId!.Trim(), ignoreCase: true, out var parsedDomain))
            {
                resolvedDomain = parsedDomain;
            }

            if (resolvedDomain is AutonomyDomain domain)
            {
                switch (domain)
                {
                    case AutonomyDomain.EmergencySurvival:
                        inferred |= ToolScope.ColonistSelf;
                        break;
                    case AutonomyDomain.BlueprintPlacement:
                    case AutonomyDomain.WorktableBills:
                        inferred |= ToolScope.ColonyBuild;
                        break;
                    case AutonomyDomain.ResourceMarking:
                    case AutonomyDomain.WorkPriorityModification:
                        inferred |= ToolScope.ColonyManage;
                        break;
                    case AutonomyDomain.HighRiskOperations:
                        inferred |= ToolScope.ColonyManage | ToolScope.ColonistSelf;
                        break;
                }
            }

            if (id.IndexOf("social", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("interaction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("dialogue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(category, "social", StringComparison.OrdinalIgnoreCase))
            {
                inferred |= ToolScope.Social;
            }

            if (manifest?.IsSelfTargeted == true ||
                string.Equals(category, "pawn", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("pawn.", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("pawn_", StringComparison.OrdinalIgnoreCase))
            {
                inferred |= ToolScope.ColonistSelf;
            }

            if (string.Equals(category, "colony", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "map", StringComparison.OrdinalIgnoreCase))
            {
                inferred |= ToolScope.ColonyBuild | ToolScope.ColonyManage;
            }

            if (inferred != ToolScope.None)
                return inferred;

            // Default fallback for custom module tools without explicit scope or domain:
            // treat as colony/self actionable unless explicitly tagged as world/meta.
            return manifest?.Kind == ToolKind.Query
                ? ToolScope.ColonyManage | ToolScope.ColonyBuild
                : ToolScope.ColonistSelf | ToolScope.ColonyManage;
        }

        public static bool MatchesScope(ToolDefinition? definition, ToolScope requestedScope)
        {
            if (definition == null || requestedScope == ToolScope.None)
                return false;
            if (requestedScope == ToolScope.All)
                return true;

            ToolScope resolved = ResolveScopes(definition);
            return (resolved & requestedScope) != ToolScope.None;
        }

        /// <summary>
        /// Projects a <see cref="ToolDefinition"/> to a compact form that strips boilerplate
        /// schema prose and optional multi-map noise while preserving tool id, types, enums, and required keys.
        /// </summary>
        public static ToolDefinition CompactDefinition(ToolDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            string compactDesc = CompactText(definition.Description, MaxCompactDescriptionChars);
            string compactSchema = CompactParametersSchema(definition.ParametersSchema);

            return definition with
            {
                Description = compactDesc,
                ParametersSchema = compactSchema
            };
        }

        private static string CompactParametersSchema(string? rawSchema)
        {
            if (string.IsNullOrWhiteSpace(rawSchema))
                return "{}";

            try
            {
                if (!(JToken.Parse(rawSchema!) is JObject root))
                    return rawSchema!;

                var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root["required"] is JArray reqArr)
                {
                    foreach (var tok in reqArr)
                    {
                        var reqKey = tok?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(reqKey))
                            requiredSet.Add(reqKey!);
                    }
                    if (reqArr.Count == 0)
                        root.Remove("required");
                }

                if (root["properties"] is JObject props)
                {
                    // Drop optional multi-map boilerplate key from compact projections
                    if (props.ContainsKey("map_id") && !requiredSet.Contains("map_id"))
                        props.Remove("map_id");

                    foreach (var prop in props.Properties().ToList())
                    {
                        if (prop.Value is JObject propObj && propObj["description"] is JValue descVal)
                        {
                            string desc = descVal.Value<string>() ?? string.Empty;
                            if (IsBoilerplateParamDesc(desc))
                            {
                                propObj.Remove("description");
                            }
                            else if (desc.Length > MaxCompactParamDescChars)
                            {
                                propObj["description"] = CompactText(desc, MaxCompactParamDescChars);
                            }
                        }
                    }
                }

                return root.ToString(Formatting.None);
            }
            catch
            {
                return rawSchema!;
            }
        }

        private static bool IsBoilerplateParamDesc(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return true;
            return desc.StartsWith("Parameter for ", StringComparison.OrdinalIgnoreCase)
                || desc.Equals("Action to perform", StringComparison.OrdinalIgnoreCase)
                || desc.Equals("Pawn thing ID", StringComparison.OrdinalIgnoreCase)
                || desc.Equals("Pawn thing ID (optional)", StringComparison.OrdinalIgnoreCase);
        }

        private static string CompactText(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string normalized = text!.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            while (normalized.Contains("  "))
                normalized = normalized.Replace("  ", " ");
            return normalized.Length <= maxChars
                ? normalized
                : normalized.Substring(0, maxChars - 1).TrimEnd() + "…";
        }
    }
}
