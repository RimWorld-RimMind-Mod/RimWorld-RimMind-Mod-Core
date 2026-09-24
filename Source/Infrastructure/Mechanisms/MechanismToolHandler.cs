using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        private readonly IMechanismReader _reader;
        private readonly IMechanismWriter _writer;
        private readonly IMechanismTrigger _trigger;
        private readonly IMechanismMetadata _metadata;
        private readonly MechanismOperationType _operation;
        private readonly IJsonExtractor _jsonExtractor;

        public MechanismToolHandler(IMechanismReader reader, IMechanismWriter writer, IMechanismTrigger trigger, IMechanismMetadata metadata, MechanismOperationType operation, IJsonExtractor jsonExtractor)
        {
            _reader = reader;
            _writer = writer;
            _trigger = trigger;
            _metadata = metadata;
            _operation = operation;
            _jsonExtractor = jsonExtractor ?? new MechanismToolHelper.FallbackJsonExtractor();
            Definition = BuildDefinition(metadata, operation);
        }

        public string Id => Definition.Id;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public ToolDefinition Definition { get; }

        public async Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
        {
            var result = await ExecuteOperationAsync(args, ct).ConfigureAwait(false);

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
                    var r = await _reader.ExecuteQueryAsync(readArgs, ct).ConfigureAwait(false);
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
                        MechanismOperationType.Set => await _writer.ExecuteSetAsync(writeArgs, ct).ConfigureAwait(false),
                        MechanismOperationType.Add => await _writer.ExecuteAddAsync(writeArgs, ct).ConfigureAwait(false),
                        MechanismOperationType.Remove => await _writer.ExecuteRemoveAsync(writeArgs, ct).ConfigureAwait(false),
                        MechanismOperationType.Toggle => await _trigger.ExecuteToggleAsync(writeArgs, ct).ConfigureAwait(false),
                        MechanismOperationType.Trigger => await _trigger.ExecuteTriggerAsync(writeArgs, ct).ConfigureAwait(false),
                        MechanismOperationType.Watch => await _trigger.ExecuteWatchAsync(writeArgs, ct).ConfigureAwait(false),
                        _ => Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(_metadata.MechanismId, _operation.ToString().ToLowerInvariant()))
                    };
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                case MechanismOperationType.List:
                {
                    var pawnId = MechanismToolHelper.ExtractInt(_jsonExtractor, args.ArgumentsJson, "pawn_id");
                    var r = await _reader.ExecuteListAsync(pawnId, ct).ConfigureAwait(false);
                    return r.IsOk
                        ? Result<object?, RimMindError>.Ok(r.Value)
                        : Result<object?, RimMindError>.Err(r.Error);
                }
                default:
                    return Result<object?, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(_metadata.MechanismId, _operation.ToString().ToLowerInvariant()));
            }
        }

        private MechanismReadArgs BuildReadArgs(ToolCallArgs args)
        {
            return new MechanismReadArgs
            {
                MechanismId = _metadata.MechanismId,
                PawnId = MechanismToolHelper.ExtractInt(_jsonExtractor, args.ArgumentsJson, "pawn_id"),
                MapId = MechanismToolHelper.ExtractNullableInt(_jsonExtractor, args.ArgumentsJson, "map_id"),
                DefName = MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "filter_def_name") ?? MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "def_name"),
                TraceId = args.TraceId
            };
        }

        private MechanismWriteArgs BuildWriteArgs(ToolCallArgs args)
        {
            return new MechanismWriteArgs
            {
                MechanismId = _metadata.MechanismId,
                PawnId = MechanismToolHelper.ExtractInt(_jsonExtractor, args.ArgumentsJson, "pawn_id"),
                MapId = MechanismToolHelper.ExtractNullableInt(_jsonExtractor, args.ArgumentsJson, "map_id"),
                DefName = MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "def_name"),
                Action = MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "action") ?? _operation.ToString().ToLowerInvariant(),
                ValueJson = MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "value") ?? MechanismToolHelper.ExtractString(_jsonExtractor, args.ArgumentsJson, "params"),
                TraceId = args.TraceId,
                Params = ExtractParamsDictionary(args.ArgumentsJson)
            };
        }

        private static Dictionary<string, string>? ExtractParamsDictionary(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var paramsToken = obj["params"];
                if (paramsToken is Newtonsoft.Json.Linq.JObject paramsObj)
                {
                    var dict = new Dictionary<string, string>();
                    foreach (var prop in paramsObj.Properties())
                    {
                        dict[prop.Name] = prop.Value?.ToString() ?? "";
                    }
                    return dict.Count > 0 ? dict : null;
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ToolDefinition BuildDefinition(IMechanismMetadata metadata, MechanismOperationType operation)
        {
            var toolId = $"{metadata.MechanismId}.{OperationSuffix(operation)}";
            var description = BuildDescription(metadata, operation);
            var schema = BuildParameterSchema(metadata, operation);

            return new ToolDefinition
            {
                Id = toolId,
                Description = description,
                ParametersSchema = schema,
                Category = metadata.Scope.ToString().ToLowerInvariant()
            };
        }

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
