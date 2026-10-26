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

        /// <summary>
        /// Lenient lookup for read/query operations: any live pawn on a loaded map or in a player caravan
        /// (prisoners, visitors and animals included). Never scans WorldPawns; cache-only off the main thread.
        /// </summary>
        protected static VersePawn? FindPawn(int pawnId)
            => RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);

        /// <summary>
        /// Strict lookup for write operations whose actor pawn id comes from the LLM: the pawn must be an
        /// active spawned player colonist (<see cref="RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist"/>).
        /// Downed colonists are accepted only when <paramref name="allowDowned"/> is true.
        /// Returns null and a descriptive <paramref name="error"/> otherwise.
        /// </summary>
        protected static VersePawn? FindWritableColonist(int pawnId, bool allowDowned, out RimMindError? error)
        {
            var pawn = FindPawn(pawnId);
            if (pawn == null)
            {
                error = RimMindErrors.PawnNotFound(pawnId);
                return null;
            }
            if (!RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is not an active spawned player colonist; prisoners, guests, enemies, dead, kidnapped or off-map pawns cannot be controlled"
                };
                return null;
            }
            if (!allowDowned && pawn.Downed)
            {
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is downed and cannot perform this action"
                };
                return null;
            }
            error = null;
            return pawn;
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
