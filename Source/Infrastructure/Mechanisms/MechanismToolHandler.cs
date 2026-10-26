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
        private readonly IJsonExtractor? _jsonExtractor;

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
            _jsonExtractor = jsonExtractor;
            Definition = BuildDefinition(metadata, operation);
        }

        public string Id => Definition.Id;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
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
            switch (_operation)
            {
                case MechanismOperationType.Query:
                {
                    var readArgs = BuildReadArgs(args);
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
                    var writeArgs = BuildWriteArgs(args);
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
                    var root = TryParseArguments(args.ArgumentsJson);
                    var pawnId = ResolvePawnId(root, args, "pawn_id");
                    var r = await _reader.ExecuteListAsync(pawnId, ct);
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                default:
                    return Result<object?, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(_metadata.MechanismId, _operation.ToString().ToLowerInvariant()));
            }
        }

        internal MechanismReadArgs BuildReadArgs(ToolCallArgs args)
        {
            var root = TryParseArguments(args.ArgumentsJson);
            return new MechanismReadArgs
            {
                MechanismId = _metadata.MechanismId,
                PawnId = ResolvePawnId(root, args, "pawn_id"),
                MapId = ExtractNullableInt(root, "map_id"),
                DefName = ExtractDefName(root, includeFilterDefName: true),
                TraceId = args.TraceId
            };
        }

        internal MechanismWriteArgs BuildWriteArgs(ToolCallArgs args)
        {
            var root = TryParseArguments(args.ArgumentsJson);
            return new MechanismWriteArgs
            {
                MechanismId = _metadata.MechanismId,
                PawnId = ResolvePawnId(root, args, "pawn_id"),
                MapId = ExtractNullableInt(root, "map_id"),
                DefName = ExtractDefName(root, includeFilterDefName: false),
                Action = ExtractString(root, "action") ?? _operation.ToString().ToLowerInvariant(),
                ValueJson = ExtractValueJson(root),
                TraceId = args.TraceId,
                Params = ExtractParamsDictionary(root)
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
        {
            var pawnId = ExtractInt(root, key, 0);
            return pawnId != 0 ? pawnId : (args.PawnId ?? 0);
        }

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
                    dict[key] = token.Type == JTokenType.String
                        ? token.Value<string>() ?? string.Empty
                        : token.ToString(Formatting.None);
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

        private static RiskLevel MapRisk(MechanismRisk risk) => risk switch
        {
            MechanismRisk.Safe => RiskLevel.Low,
            MechanismRisk.Moderate => RiskLevel.Medium,
            MechanismRisk.Dangerous => RiskLevel.High,
            _ => RiskLevel.Medium
        };

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
