using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Features.Context;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IContextLayerBuilder
    {
        Task<List<ContextEntry>> BuildLayerAsync(List<KeyMeta> keys, object? pawn, ProviderContext ctx, ProviderCache? cache, CancellationToken ct);
        ChatMessage? EntriesToLayerMessage(List<ContextEntry> entries, string layerTag);
    }
}
