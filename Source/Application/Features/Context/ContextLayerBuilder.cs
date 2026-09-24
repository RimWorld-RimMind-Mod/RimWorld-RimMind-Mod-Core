using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class ContextLayerBuilder : IContextLayerBuilder
    {
        public async Task<List<ContextEntry>> BuildLayerAsync(
            List<KeyMeta> keys, object? pawn, ProviderContext ctx,
            ProviderCache? cache, CancellationToken ct)
        {
            if (keys == null || keys.Count == 0) return new List<ContextEntry>();
            var entries = new List<ContextEntry>();
            foreach (var key in keys)
            {
                ct.ThrowIfCancellationRequested();
                if (key.Def is ContextProviderDef def)
                {
                    string? value;
                    if (cache != null)
                    {
                        ProviderCache.ProviderCacheResult cachedResult = await cache
                            .GetOrComputeWithOutcomeAsync(def, ctx, ct)
                            .ConfigureAwait(false);
                        if (cachedResult.ProviderFaulted)
                            throw new ContextProviderFaultException(def.Key);
                        value = cachedResult.Value;
                    }
                    else
                    {
                        value = await def.Provider(ctx, ct).ConfigureAwait(false);
                    }
                    if (value != null)
                        entries.Add(new ContextEntry { SourceKey = key.Key, Content = value });
                }
                else if (key.ValueProvider != null)
                {
                    var result = key.ValueProvider(pawn!);
                    entries.AddRange(result);
                }
            }
            return entries;
        }

        public ChatMessage? EntriesToLayerMessage(List<ContextEntry> entries, string layerTag)
        {
            if (entries == null || entries.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"<layer_{layerTag}>");
            bool hasContent = false;
            var orderedEntries = entries.OrderBy(e => e.SourceKey, System.StringComparer.Ordinal);
            foreach (var entry in orderedEntries)
            {
                if (!string.IsNullOrEmpty(entry.Content))
                {
                    sb.AppendLine($"[{entry.SourceKey}] {entry.Content}");
                    hasContent = true;
                }
            }
            sb.AppendLine($"</layer_{layerTag}>");
            if (!hasContent) return null;
            return new ChatMessage { Role = "system", Content = sb.ToString(), LayerTag = layerTag };
        }

        internal sealed class ContextProviderFaultException : System.Exception
        {
            public ContextProviderFaultException(string providerKey)
                : base($"Context provider failed: {providerKey}")
            {
            }
        }
    }
}
