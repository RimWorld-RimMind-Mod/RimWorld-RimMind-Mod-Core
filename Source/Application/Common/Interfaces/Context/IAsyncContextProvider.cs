using System.Threading;
using System.Threading.Tasks;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Optional interface for class-style async context providers.
    /// Sub-mods can either implement this interface or pass a Provider delegate in ContextProviderDef.
    /// </summary>
    public interface IAsyncContextProvider
    {
        ContextProviderDef Definition { get; }
        Task<string?> ProvideAsync(ProviderContext ctx, CancellationToken ct);
    }
}
