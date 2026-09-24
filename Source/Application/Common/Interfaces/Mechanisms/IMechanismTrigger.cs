using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Mechanisms
{
    public interface IMechanismTrigger
    {
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct);
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct);
        [ThreadAffinity(ThreadAffinityKind.MainOnly)]
        Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct);
    }
}
