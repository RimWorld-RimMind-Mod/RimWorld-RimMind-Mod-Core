using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Thought
{
    public sealed class ThoughtMechanism : GameMechanismBase<ThoughtDef>
    {
        public override string MechanismId => "pawn.thought";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Add, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and add pawn thoughts and memories",
            QueryDescription = "Query pawn's current thoughts and memories. Optionally filter by def_name.",
            AddDescription = "Add a thought/memory to the pawn. Provide def_name of the ThoughtDef.",
            ListDescription = "List all thought definitions (large enum, >200 entries)"
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var thought = pawn.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(FindDef(args.DefName));
                if (thought == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new { def = thought.def.defName, label = thought.LabelCap, moodOffset = thought.MoodOffset() };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var thoughts = pawn.needs?.mood?.thoughts?.memories?.Memories?
                .Select(t => new { def = t.def.defName, label = t.LabelCap, moodOffset = t.MoodOffset(), age = t.age })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(thoughts ?? new object())));
        }

        public override Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var thoughtDef = FindDef(args.DefName);
            if (thoughtDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
