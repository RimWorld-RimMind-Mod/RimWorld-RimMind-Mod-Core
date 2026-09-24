using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using VersePawn = Verse.Pawn;
using VerseMap = Verse.Map;

namespace RimMind.Infrastructure.Mechanisms
{
    public abstract class GameMechanismBaseNoDef : IGameMechanism
    {
        string IExtension.Id => MechanismId;
        string IExtension.OwnerModId => RimMindOwnerConsts.CoreModId;
        public abstract string MechanismId { get; }
        public abstract MechanismScope Scope { get; }
        public abstract MechanismRisk Risk { get; }
        public abstract IReadOnlyList<MechanismOperationType> SupportedOperations { get; }
        public abstract MechanismDocs Docs { get; }
        public virtual IReadOnlyList<MechanismActionInfo>? GetWriteActions() => null;
        public virtual MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;

        public virtual Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
            => Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "query")));

        public virtual Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "set")));

        public virtual Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "add")));

        public virtual Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "remove")));

        public virtual Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "toggle")));

        public virtual Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "trigger")));

        public virtual Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
            => Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "list")));

        public virtual Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct)
            => Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(MechanismId, "watch")));

        protected static VersePawn? FindPawn(int pawnId)
        {
            foreach (var map in Find.Maps)
            {
                var pawn = map.mapPawns?.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
                if (pawn != null) return pawn;
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == pawnId);
            return worldPawn;
        }

        protected static VerseMap? ResolveMap(MechanismReadArgs args)
        {
            if (args.MapId.HasValue)
            {
                foreach (var map in Find.Maps)
                {
                    if (map.uniqueID == args.MapId.Value)
                        return map;
                }
                return null;
            }
            return Find.AnyPlayerHomeMap;
        }

        protected static VerseMap? ResolveMap(MechanismWriteArgs args)
        {
            if (args.MapId.HasValue)
            {
                foreach (var map in Find.Maps)
                {
                    if (map.uniqueID == args.MapId.Value)
                        return map;
                }
                return null;
            }
            return Find.AnyPlayerHomeMap;
        }
    }
}
