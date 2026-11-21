using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Work
{
    public sealed class WorkMechanism : GameMechanismBase<WorkTypeDef>
    {
        public override string MechanismId => "pawn.work";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and modify pawn work priorities",
            QueryDescription = "Query pawn work priorities for all work types. Optionally filter by def_name.",
            SetDescription = "Set or batch-update pawn work priorities (0-4). Supports single def_name+value, action 'set_batch' with priorities_json (map or array), or 'reset_defaults'.",
            ListDescription = "List all work type definitions"
        };

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo { Action = "set_priority", Description = "Set a single WorkType priority (0=off, 1=highest .. 4=lowest)", DefNameHint = "WorkTypeDef", RequiredParams = new List<string> { "def_name" }.AsReadOnly() },
                new MechanismActionInfo { Action = "set_batch", Description = "Batch set multiple WorkType priorities via priorities_json map or array", RequiredParams = new List<string> { "priorities_json" }.AsReadOnly() },
                new MechanismActionInfo { Action = "reset_defaults", Description = "Reset all enabled visible work types to default priority 3" }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override AutonomyDomain? GetAutonomyDomainForOperation(MechanismOperationType operation)
            => operation == MechanismOperationType.Set ? AutonomyDomain.WorkPriorityModification : null;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "pawn cannot work")));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var workDef = FindDef(args.DefName);
                if (workDef == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new { def = workDef.defName, label = workDef.label, priority = pawn.workSettings.GetPriority(workDef) };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .Select(w => new { def = w.defName, label = w.label, priority = pawn.workSettings.GetPriority(w) })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(workTypes)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindWritableColonist(args.PawnId, allowDowned: true, out var pawnError);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(pawnError!));

            if (RimMindAPI.Autonomy.IsDisabled(pawn, AutonomyDomain.WorkPriorityModification))
            {
                return Task.FromResult(Result<bool, RimMindError>.Err(
                    RimMindErrors.InvalidAction(MechanismId, $"Modifying Work Tab priorities is disabled for {pawn.LabelShort} by player autonomy policy.")));
            }

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "pawn cannot work")));

            string effectiveAction = (args.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (effectiveAction == "reset_defaults")
            {
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (wt != null && wt.visible && !pawn.WorkTypeIsDisabled(wt))
                        pawn.workSettings.SetPriority(wt, 3);
                }
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

            string? batchJson = ExtractBatchPayload(args);
            if (effectiveAction == "set_batch" || !string.IsNullOrWhiteSpace(batchJson))
            {
                return Task.FromResult(ApplyBatchPriorities(pawn, batchJson));
            }

            string? rawDefName = args.DefName;
            if (string.IsNullOrEmpty(rawDefName) && args.Params != null)
            {
                if (!args.Params.TryGetValue("def_name", out rawDefName) || string.IsNullOrEmpty(rawDefName))
                    args.Params.TryGetValue("work_type", out rawDefName);
            }

            if (string.IsNullOrEmpty(rawDefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var workDef = ResolveWorkTypeDef(rawDefName!);
            if (workDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(rawDefName!)));

            if (pawn.WorkTypeIsDisabled(workDef))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, $"Work type '{workDef.defName}' is disabled for {pawn.LabelShort}")));

            int priority = 1;
            if (!string.IsNullOrEmpty(args.ValueJson))
            {
                if (!int.TryParse(args.ValueJson, out priority))
                {
                    if (args.Params != null && args.Params.TryGetValue("priority", out var pStr) && int.TryParse(pStr, out var parsedP))
                        priority = parsedP;
                    else
                        priority = 1;
                }
            }
            else if (args.Params != null && args.Params.TryGetValue("priority", out var pParam) && int.TryParse(pParam, out var parsedParam))
            {
                priority = parsedParam;
            }

            if (priority < 0 || priority > 4)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "priority must be 0-4")));

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.SetPriority(workDef, priority);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        private static string? ExtractBatchPayload(MechanismWriteArgs args)
        {
            if (args.Params != null && args.Params.TryGetValue("priorities_json", out var pJson) && !string.IsNullOrWhiteSpace(pJson))
                return pJson;

            if (!string.IsNullOrWhiteSpace(args.ValueJson))
            {
                string trimmed = args.ValueJson!.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                    return trimmed;
            }

            if (!string.IsNullOrWhiteSpace(args.RawArgumentsJson))
            {
                try
                {
                    var obj = JObject.Parse(args.RawArgumentsJson!);
                    var tok = obj["priorities"] ?? obj["priorities_json"];
                    if (tok != null && (tok.Type == JTokenType.Object || tok.Type == JTokenType.Array))
                        return tok.ToString(Formatting.None);
                    if (tok != null && tok.Type == JTokenType.String)
                        return tok.Value<string>();
                }
                catch
                {
                    // Ignore malformed outer JSON
                }
            }

            return null;
        }

        private Result<bool, RimMindError> ApplyBatchPriorities(global::Verse.Pawn pawn, string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "set_batch requires priorities_json or priorities object/array."));

            var updates = new List<(WorkTypeDef Def, int Priority)>();
            try
            {
                JToken token = JToken.Parse(json!);
                if (token is JObject mapObj)
                {
                    if (mapObj["priorities"] is JToken nested)
                        token = nested;
                }

                if (token is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        if (!TryValidatePriorityEntry(pawn, prop.Name, prop.Value.Value<int>(), out var def, out var err))
                            return Result<bool, RimMindError>.Err(err!);
                        updates.Add((def!, prop.Value.Value<int>()));
                    }
                }
                else if (token is JArray arr)
                {
                    foreach (var item in arr.OfType<JObject>())
                    {
                        string? name = item["work_type"]?.Value<string>() ?? item["def_name"]?.Value<string>();
                        int? prio = item["priority"]?.Value<int>();
                        if (string.IsNullOrWhiteSpace(name) || !prio.HasValue)
                            return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "Each batch item must specify work_type/def_name and priority (0-4)."));
                        if (!TryValidatePriorityEntry(pawn, name!, prio.Value, out var def, out var err))
                            return Result<bool, RimMindError>.Err(err!);
                        updates.Add((def!, prio.Value));
                    }
                }
                else
                {
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "priorities payload must be JSON object or array."));
                }
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, $"Invalid batch priorities JSON: {ex.Message}"));
            }

            if (updates.Count == 0)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "No valid work priorities found in batch payload."));

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            foreach (var (def, priority) in updates)
                pawn.workSettings.SetPriority(def, priority);

            return Result<bool, RimMindError>.Ok(true);
        }

        private bool TryValidatePriorityEntry(global::Verse.Pawn pawn, string rawName, int priority, out WorkTypeDef? workDef, out RimMindError? error)
        {
            workDef = ResolveWorkTypeDef(rawName);
            if (workDef == null)
            {
                error = RimMindErrors.InvalidDefName(rawName);
                return false;
            }
            if (pawn.WorkTypeIsDisabled(workDef))
            {
                error = RimMindErrors.InvalidAction(MechanismId, $"Work type '{workDef.defName}' is disabled for {pawn.LabelShort}");
                return false;
            }
            if (priority < 0 || priority > 4)
            {
                error = RimMindErrors.InvalidAction(MechanismId, $"Priority {priority} for '{workDef.defName}' must be 0-4");
                return false;
            }
            error = null;
            return true;
        }

        private WorkTypeDef? ResolveWorkTypeDef(string rawName)
        {
            var trimmed = rawName.Trim();
            var direct = FindDef(trimmed);
            if (direct != null) return direct;

            string alias = trimmed.ToLowerInvariant() switch
            {
                "haul" or "hauling" or "haultostorage" => "Hauling",
                "clean" or "cleaning" => "Cleaning",
                "construct" or "construction" or "build" or "repair" => "Construction",
                "plantcut" or "plantcutting" or "cutplants" or "chopwood" => "PlantCutting",
                "grow" or "growing" or "sow" or "harvest" => "Growing",
                "cook" or "cooking" => "Cooking",
                "mine" or "mining" => "Mining",
                "doctor" or "doctoring" or "tend" or "medical" => "Doctor",
                "research" or "researching" => "Research",
                "hunt" or "hunting" => "Hunting",
                "craft" or "crafting" => "Crafting",
                _ => trimmed
            };

            return FindDef(alias) ?? DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault(w =>
                string.Equals(w.defName, alias, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.labelShort, trimmed, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.gerundLabel, trimmed, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
