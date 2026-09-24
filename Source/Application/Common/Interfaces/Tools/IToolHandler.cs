using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Tools
{
    public interface IToolHandler : IExtension
    {
        ToolDefinition Definition { get; }

        [ThreadAffinity(ThreadAffinityKind.Any)]
        Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct);
    }
}
