using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Mechanisms
{
    public interface IMechanismWriter
    {
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct);
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct);
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct);
    }
}
