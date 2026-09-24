using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Mechanisms
{
    public interface IMechanismReader
    {
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct);
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct);
    }
}
