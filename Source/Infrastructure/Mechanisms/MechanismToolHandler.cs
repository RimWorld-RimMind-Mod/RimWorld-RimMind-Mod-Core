using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Mechanisms
{
    public sealed class MechanismToolHandler : IToolHandler
    {
        private static readonly string[] DefNameAliasKeys =
        {
            "def_name",
            "thing_def",
            "job_def",
            "thought_def",
            "mental_state_def",
            "inspiration_def",
            "skill_def",
            "work_type"
        };

        private static readonly string[] FlatParamKeys =
        {
            "def_name",
            "thing_def",
            "job_def",
            "thought_def",
            "mental_state_def",
            "inspiration_def",
            "skill_def",
            "work_type",
            "target_id",
            "target_pawn_id",
            "count",
            "priority",
            "level",
            "value",
            "enabled",
            "drafted",
            "forced",
            "x",
            "z",
            "cell_x",
            "cell_z"
        };

        private readonly IMechanismReader _reader;
        private readonly IMechanismWriter _writer;
        private readonly IMechanismTrigger _trigger;
        private readonly IMechanismMetadata _metadata;
        private readonly MechanismOperationType _operation;

        /// <param name="jsonExtractor">Unused; kept for constructor compatibility. Arguments are parsed once as a JObject.</param>
        public MechanismToolHandler(
            IMechanismReader reader,
            IMechanismWriter writer,
            IMechanismTrigger trigger,
            IMechanismMetadata metadata,
            MechanismOperationType operation,
            IJsonExtractor? jsonExtractor = null)
        {
            _reader = reader;
            _writer = writer;
            _trigger = trigger;
            _metadata = metadata;
            _operation = operation;
            Definition = BuildDefinition(metadata, operation);
        }

        public string Id => Definition.Id;
        public string OwnerModId => string.IsNullOrWhiteSpace(_metadata?.OwnerModId)
            ? RimMindOwnerConsts.CoreModId
            : _metadata.OwnerModId;
        public ToolDefinition Definition { get; }

        public async Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
        {
            var result = await ExecuteOperationAsync(args, ct);

            if (result.IsErr)
            {
                return Result<ToolResult, RimMindError>.Ok(new ToolResult
                {
                    ToolCallId = args.ToolCallId,
                    Content = result.Error.Message,
                    IsError = true
                });
            }

            var content = _operation == MechanismOperationType.List
                ? JsonConvert.SerializeObject(result.Value)
                : result.Value?.ToString() ?? "";

            return Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                Content = content,
                IsError = false
            });
        }

        private async Task<Result<object?, RimMindError>> ExecuteOperationAsync(ToolCallArgs args, CancellationToken ct)
        {
            var root = TryParseArguments(args.ArgumentsJson);
            var target = ResolveTargetPawn(root, args);
            if (!target.IsValid)
            {
                // An explicit but unparseable pawn_id must never be redirected to the calling pawn.
                return Result<object?, RimMindError>.Err(InvalidPawnTargetError(Id, target));
            }

            switch (_operation)
            {
                case MechanismOperationType.Query:
                {
                    var readArgs = BuildReadArgs(root, args, target.PawnId);
                    var r = await _reader.ExecuteQueryAsync(readArgs, ct);
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                case MechanismOperationType.Set:
                case MechanismOperationType.Add:
                case MechanismOperationType.Remove:
                case MechanismOperationType.Toggle:
                case MechanismOperationType.Trigger:
                case MechanismOperationType.Watch:
                {
                    var writeArgs = BuildWriteArgs(root, args, target.PawnId);
                    var r = _operation switch
                    {
                        MechanismOperationType.Set => await _writer.ExecuteSetAsync(writeArgs, ct),
                        MechanismOperationType.Add => await _writer.ExecuteAddAsync(writeArgs, ct),
                        MechanismOperationType.Remove => await _writer.ExecuteRemoveAsync(writeArgs, ct),
                        MechanismOperationType.Toggle => await _trigger.ExecuteToggleAsync(writeArgs, ct),
                        MechanismOperationType.Trigger => await _trigger.ExecuteTriggerAsync(writeArgs, ct),
                        MechanismOperationType.Watch => await _trigger.ExecuteWatchAsync(writeArgs, ct),
                        _ => Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(_metadata.MechanismId, _operation.ToString().ToLowerInvariant()))
                    };
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                case MechanismOperationType.List:
                {
                    var r = await _reader.ExecuteListAsync(target.PawnId, ct);
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                default:
                    return Result<object?, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(_metadata.MechanismId, _operation.ToString().ToLowerInvariant()));
            }
        }

        /// <summary>
        /// Builds read args. An explicit but unparseable pawn_id yields PawnId 0 here; the execution path
        /// rejects such calls before building args.
        /// </summary>
        internal MechanismReadArgs BuildReadArgs(ToolCallArgs args)
        {
            var root = TryParseArguments(args.ArgumentsJson);
            return BuildReadArgs(root, args, ResolveTargetPawn(root, args).PawnId);
        }

        private MechanismReadArgs BuildReadArgs(JObject? root, ToolCallArgs args, int pawnId)
        {
            return new MechanismReadArgs
            {
                MechanismId = _metadata.MechanismId,
                PawnId = pawnId,
                MapId = ExtractNullableInt(root, "map_id"),
                DefName = ExtractDefName(root, includeFilterDefName: true),
                TraceId = args.TraceId
            };
        }

        /// <summary>
        /// Builds write args. An explicit but unparseable pawn_id yields PawnId 0 here; the execution path
        /// rejects such calls before building args.
        /// </summary>
        internal MechanismWriteArgs BuildWriteArgs(ToolCallArgs args)
        {
            var root = TryParseArguments(args.ArgumentsJson);
            return BuildWriteArgs(root, args, ResolveTargetPawn(root, args).PawnId);
        }

        private MechanismWriteArgs BuildWriteArgs(JObject? root, ToolCallArgs args, int pawnId)
        {
            var writeActions = _metadata?.GetWriteActions();
            var paramsDict = ExtractParamsDictionary(root, writeActions);
            var defName = ExtractDefName(root, includeFilterDefName: false);
            if (string.IsNullOrEmpty(defName)
                && paramsDict != null
                && paramsDict.TryGetValue("def_name", out var mappedDefName)
                && !string.IsNullOrEmpty(mappedDefName))
            {
                defName = mappedDefName;
            }

            return new MechanismWriteArgs
            {
                MechanismId = _metadata!.MechanismId,
                PawnId = pawnId,
                MapId = ExtractNullableInt(root, "map_id"),
                DefName = defName,
                Action = ExtractString(root, "action") ?? _operation.ToString().ToLowerInvariant(),
                ValueJson = ExtractValueJson(root),
                TraceId = args.TraceId,
                Params = paramsDict
            };
        }

        internal static JObject? TryParseArguments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JObject.Parse(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static int ResolvePawnId(JObject? root, ToolCallArgs args, string key = "pawn_id")
            => ResolveTargetPawn(root, args, key).PawnId;

        /// <summary>
        /// Resolves the target pawn through the shared <see cref="PawnIdParser"/> (root or nested <c>params</c>;
        /// accepts <c>12</c>, <c>"Pawn_12"</c>, <c>"NPC-12"</c>; falls back to the caller's PawnId, then NpcId).
        /// </summary>
        internal static PawnTargetResolution ResolveTargetPawn(JObject? root, ToolCallArgs args, string key = "pawn_id")
            => PawnIdParser.ResolveTarget(root, key, args.PawnId, args.NpcId);

        internal static RimMindError InvalidPawnTargetError(string toolId, PawnTargetResolution target)
            => RimMindErrors.ToolExecution(
                toolId,
                $"pawn_id '{target.InvalidValue}' is not a valid pawn id (expected e.g. 12, \"Pawn_12\" or \"NPC-12\")");

        internal static string? ExtractDefName(JObject? root, bool includeFilterDefName)
        {
            if (root == null)
            {
                return null;
            }

            if (includeFilterDefName)
            {
                var filterDef = ExtractString(root, "filter_def_name");
                if (!string.IsNullOrEmpty(filterDef))
                {
                    return filterDef;
                }
            }

            for (int i = 0; i < DefNameAliasKeys.Length; i++)
            {
                var value = ExtractString(root, DefNameAliasKeys[i]);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
        }

        internal static string? ExtractValueJson(JObject? root)
        {
            if (root == null)
            {
                return null;
            }

            var direct = ExtractString(root, "value") ?? ExtractString(root, "params");
            if (direct != null)
            {
                return direct;
            }

            if (TryGetToken(root, "priority", out _))
            {
                var pInt = ExtractNullableInt(root, "priority");
                return pInt.HasValue ? pInt.Value.ToString(CultureInfo.InvariantCulture) : ExtractString(root, "priority");
            }

            if (TryGetToken(root, "level", out _))
            {
                var lInt = ExtractNullableInt(root, "level");
                return lInt.HasValue ? lInt.Value.ToString(CultureInfo.InvariantCulture) : ExtractString(root, "level");
            }

            if (TryGetToken(root, "count", out _))
            {
                var cInt = ExtractNullableInt(root, "count");
                return cInt.HasValue ? cInt.Value.ToString(CultureInfo.InvariantCulture) : ExtractString(root, "count");
            }

            if (TryGetToken(root, "enabled", out _))
            {
                return ExtractBool(root, "enabled") ? "true" : "false";
            }

            if (TryGetToken(root, "drafted", out _))
            {
                return ExtractBool(root, "drafted") ? "true" : "false";
            }

            if (TryGetToken(root, "forced", out _))
            {
                return ExtractBool(root, "forced") ? "true" : "false";
            }

            return null;
        }

        internal static string? ExtractString(JObject? root, string key)
        {
            if (!TryGetToken(root, key, out var token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
        }

        internal static int ExtractInt(JObject? root, string key, int defaultValue = 0)
        {
            return ExtractNullableInt(root, key) ?? defaultValue;
        }

        internal static int? ExtractNullableInt(JObject? root, string key)
        {
            if (!TryGetToken(root, key, out var token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            if (token.Type == JTokenType.Float)
            {
                return (int)token.Value<double>();
            }

            var raw = token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        internal static float ExtractFloat(JObject? root, string key, float defaultValue = 0f)
        {
            if (!TryGetToken(root, key, out var token) || token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return token.Value<float>();
            }

            var raw = token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
            return float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        internal static bool ExtractBool(JObject? root, string key, bool defaultValue = false)
        {
            if (!TryGetToken(root, key, out var token) || token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() != 0;
            }

            var raw = token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
            if (bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            {
                return intVal != 0;
            }

            return defaultValue;
        }

        internal static Dictionary<string, string>? ExtractParamsDictionary(JObject? root)
            => ExtractParamsDictionary(root, null);

        internal static Dictionary<string, string>? ExtractParamsDictionary(
            JObject? root,
            IReadOnlyList<MechanismActionInfo>? writeActions)
        {
            if (root == null)
            {
                return null;
            }

            Dictionary<string, string>? dict = null;

            if (root["params"] is JObject paramsObj)
            {
                foreach (var prop in paramsObj.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    dict ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    dict[prop.Name] = prop.Value.Type == JTokenType.String
                        ? prop.Value.Value<string>() ?? string.Empty
                        : prop.Value.ToString();
                }
            }

            for (int i = 0; i < FlatParamKeys.Length; i++)
            {
                var key = FlatParamKeys[i];
                var token = root[key];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                dict ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (!dict.ContainsKey(key))
                {
                    dict[key] = FormatTokenValue(token);
                }
            }

            if (writeActions != null)
            {
                foreach (var wa in writeActions)
                {
                    if (wa?.ParameterDescriptors == null) continue;
                    foreach (var pd in wa.ParameterDescriptors)
                    {
                        if (pd == null || string.IsNullOrWhiteSpace(pd.Name)) continue;

                        if (TryGetToken(root, pd.Name, out var directToken) && directToken != null)
                        {
                            dict ??= new Dictionary<string, string>(StringComparer.Ordinal);
                            if (!dict.ContainsKey(pd.Name))
                            {
                                dict[pd.Name] = FormatTokenValue(directToken);
                            }
                        }

                        if (pd.Aliases == null) continue;
                        foreach (var alias in pd.Aliases)
                        {
                            if (string.IsNullOrWhiteSpace(alias)) continue;
                            if (!TryGetToken(root, alias, out var aliasToken) || aliasToken == null) continue;

                            var formatted = FormatTokenValue(aliasToken);
                            dict ??= new Dictionary<string, string>(StringComparer.Ordinal);
                            if (!dict.ContainsKey(alias))
                            {
                                dict[alias] = formatted;
                            }
                            if (!dict.ContainsKey(pd.Name))
                            {
                                dict[pd.Name] = formatted;
                            }
                        }
                    }
                }
            }

            if (dict != null)
            {
                if (dict.TryGetValue("target_id", out var targetId) && !dict.ContainsKey("target_pawn_id"))
                {
                    dict["target_pawn_id"] = targetId;
                }

                if (dict.TryGetValue("x", out var xVal) && !dict.ContainsKey("cell_x"))
                {
                    dict["cell_x"] = xVal;
                }

                if (dict.TryGetValue("z", out var zVal) && !dict.ContainsKey("cell_z"))
                {
                    dict["cell_z"] = zVal;
                }
            }

            return dict != null && dict.Count > 0 ? dict : null;
        }

        private static string FormatTokenValue(JToken token)
        {
            return token.Type == JTokenType.String
                ? token.Value<string>() ?? string.Empty
                : token.ToString(Formatting.None);
        }

        private static bool TryGetToken(JObject? root, string key, out JToken? token)
        {
            token = null;
            if (root == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (root.TryGetValue(key, StringComparison.Ordinal, out token) && token != null && token.Type != JTokenType.Null)
            {
                return true;
            }

            if (root["params"] is JObject paramsObj
                && paramsObj.TryGetValue(key, StringComparison.Ordinal, out token)
                && token != null
                && token.Type != JTokenType.Null)
            {
                return true;
            }

            token = null;
            return false;
        }

        private static ToolDefinition BuildDefinition(IMechanismMetadata metadata, MechanismOperationType operation)
        {
            var toolId = $"{metadata.MechanismId}.{OperationSuffix(operation)}";
            var description = BuildDescription(metadata, operation);
            var schema = BuildParameterSchema(metadata, operation);
            var mechanismRisk = metadata.GetRiskForOperation(operation);

            return new ToolDefinition
            {
                Id = toolId,
                Description = description,
                ParametersSchema = schema,
                Category = metadata.Scope.ToString().ToLowerInvariant(),
                Manifest = new ToolManifest
                {
                    RiskLevel = MapRisk(mechanismRisk),
                    RequiresApproval = mechanismRisk >= MechanismRisk.Dangerous
                }
            };
        }

        private static RiskLevel MapRisk(MechanismRisk risk) => ToolManifestRiskResolver.MapMechanismRisk(risk);

        private static string BuildDescription(IMechanismMetadata metadata, MechanismOperationType operation)
        {
            var desc = operation switch
            {
                MechanismOperationType.Query => metadata.Docs.QueryDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Set => metadata.Docs.SetDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Add => metadata.Docs.AddDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Remove => metadata.Docs.RemoveDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Toggle => metadata.Docs.ToggleDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Trigger => metadata.Docs.TriggerDescription ?? metadata.Docs.Summary,
                MechanismOperationType.List => metadata.Docs.ListDescription ?? metadata.Docs.Summary,
                MechanismOperationType.Watch => metadata.Docs.WatchDescription ?? metadata.Docs.Summary,
                _ => metadata.Docs.Summary
            };

            if (metadata.GetRiskForOperation(operation) == MechanismRisk.Dangerous)
            {
                desc = $"[DANGEROUS] {desc}";
            }

            return desc;
        }

        private static string BuildParameterSchema(IMechanismMetadata metadata, MechanismOperationType operation)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            if (metadata.Scope == MechanismScope.Pawn)
            {
                properties["pawn_id"] = new { type = "integer", description = "Pawn thing ID" };
                required.Add("pawn_id");
                properties["map_id"] = new { type = "integer", description = "Optional map ID for multi-map scenarios" };
            }
            else if (metadata.Scope == MechanismScope.Map || metadata.Scope == MechanismScope.Colony)
            {
                properties["pawn_id"] = new { type = "integer", description = "Pawn thing ID (optional)" };
                properties["map_id"] = new { type = "integer", description = "Optional map ID; defaults to current map" };
            }
            else
            {
                properties["map_id"] = new { type = "integer", description = "Optional map ID for map-specific queries" };
            }

            switch (operation)
            {
                case MechanismOperationType.Query:
                    properties["filter_def_name"] = new { type = "string", description = "Optional def name to filter" };
                    break;

                case MechanismOperationType.Set:
                case MechanismOperationType.Add:
                case MechanismOperationType.Remove:
                case MechanismOperationType.Toggle:
                    properties["def_name"] = new { type = "string", description = "Def name to target" };
                    properties["value"] = new { type = "string", description = "Value for the operation (JSON)" };
                    break;

                case MechanismOperationType.Trigger:
                    properties["def_name"] = new { type = "string", description = "Optional def name to target" };
                    properties["params"] = new { type = "object", description = "Optional parameters for the trigger" };
                    break;

                case MechanismOperationType.List:
                    break;

                case MechanismOperationType.Watch:
                    properties["def_name"] = new { type = "string", description = "Optional def name to watch" };
                    break;
            }

            var writeActions = metadata.GetWriteActions();
            if (writeActions != null && writeActions.Count > 0
                && (operation == MechanismOperationType.Set
                    || operation == MechanismOperationType.Add
                    || operation == MechanismOperationType.Remove
                    || operation == MechanismOperationType.Toggle
                    || operation == MechanismOperationType.Trigger))
            {
                properties["action"] = new
                {
                    type = "string",
                    @enum = writeActions.Select(a => a.Action).ToArray(),
                    description = "Action to perform"
                };
                required.Add("action");

                foreach (var wa in writeActions)
                {
                    if (wa.ParameterDescriptors != null)
                    {
                        foreach (var pd in wa.ParameterDescriptors)
                        {
                            if (pd == null || string.IsNullOrWhiteSpace(pd.Name)) continue;
                            var jsonType = string.IsNullOrWhiteSpace(pd.JsonType) ? "string" : pd.JsonType;
                            var desc = !string.IsNullOrWhiteSpace(pd.Description)
                                ? pd.Description
                                : $"Parameter for {wa.Action}";
                            properties[pd.Name] = new { type = jsonType, description = desc };
                            if (pd.Required && !required.Contains(pd.Name))
                            {
                                required.Add(pd.Name);
                            }
                        }
                    }

                    if (wa.RequiredParams == null) continue;
                    foreach (var p in wa.RequiredParams)
                    {
                        if (!properties.ContainsKey(p))
                        {
                            properties[p] = new { type = "string", description = $"Parameter for {wa.Action}" };
                        }
                    }
                }
            }

            var schema = new
            {
                type = "object",
                properties,
                required = required.ToArray()
            };

            return JsonConvert.SerializeObject(schema);
        }

        private static string OperationSuffix(MechanismOperationType operation)
        {
            return operation switch
            {
                MechanismOperationType.Query => "query",
                MechanismOperationType.Set => "set",
                MechanismOperationType.Add => "add",
                MechanismOperationType.Remove => "remove",
                MechanismOperationType.Toggle => "toggle",
                MechanismOperationType.Trigger => "trigger",
                MechanismOperationType.List => "list",
                MechanismOperationType.Watch => "watch",
                _ => operation.ToString().ToLowerInvariant()
            };
        }
    }
}
