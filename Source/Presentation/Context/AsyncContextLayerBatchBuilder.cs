using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Context
{
    /// <summary>
    /// Builds the asynchronous context layers as one fault-isolated batch.
    /// A fault in one layer is represented in its outcome and does not discard
    /// the entries produced by the other layers.
    /// </summary>
    internal sealed class AsyncContextLayerBatchBuilder
    {
        private readonly IContextLayerBuilder _layerBuilder;
        private readonly ProviderCache? _providerCache;
        private readonly ILogSink _logSink;

        public AsyncContextLayerBatchBuilder(
            IContextLayerBuilder layerBuilder,
            ProviderCache? providerCache,
            ILogSink logSink)
        {
            _layerBuilder = layerBuilder;
            _providerCache = providerCache;
            _logSink = logSink;
        }

        public async Task<AsyncContextLayerBatch> BuildAsync(
            BudgetAllocation schedule,
            object? pawn,
            ProviderContext providerContext,
            string npcId,
            string scenario,
            ISet<string>? skipLayers,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Task<ContextLayerBuildOutcome> l0Task = BuildLayerAsync("L0", schedule.L0Keys, pawn, providerContext, npcId, scenario, ct);
            Task<ContextLayerBuildOutcome> l1Task = BuildLayerAsync("L1", schedule.L1Keys, pawn, providerContext, npcId, scenario, ct);
            Task<ContextLayerBuildOutcome> l2Task = BuildLayerAsync("L2", schedule.L2Keys, pawn, providerContext, npcId, scenario, ct);
            Task<ContextLayerBuildOutcome> l3Task = skipLayers != null && skipLayers.Contains("L3")
                ? Task.FromResult(ContextLayerBuildOutcome.Skipped("L3"))
                : BuildLayerAsync("L3", schedule.L3Keys, pawn, providerContext, npcId, scenario, ct);
            Task<ContextLayerBuildOutcome> l5Task = BuildLayerAsync("L5", schedule.L5Keys, pawn, providerContext, npcId, scenario, ct);

            ContextLayerBuildOutcome[] outcomes = await Task.WhenAll(l0Task, l1Task, l2Task, l3Task, l5Task).ConfigureAwait(false);
            return new AsyncContextLayerBatch(outcomes[0], outcomes[1], outcomes[2], outcomes[3], outcomes[4]);
        }

        private async Task<ContextLayerBuildOutcome> BuildLayerAsync(
            string layer,
            List<KeyMeta> keys,
            object? pawn,
            ProviderContext providerContext,
            string npcId,
            string scenario,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                List<ContextEntry> entries = await _layerBuilder
                    .BuildLayerAsync(keys, pawn, providerContext, _providerCache, ct)
                    .ConfigureAwait(false);
                return ContextLayerBuildOutcome.Succeeded(layer, entries, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a request-level outcome and must not be converted to an empty layer.
                throw;
            }
            catch (Exception ex)
            {
                _logSink.LogFromBackground(
                    $"[RimMind-Core] Context layer build failed: layer={layer}, npc={npcId}, scenario={scenario}, elapsedMs={stopwatch.ElapsedMilliseconds}, exception={ex.GetType().Name}",
                    isWarning: true);
                return ContextLayerBuildOutcome.Failed(layer, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    internal sealed record ContextLayerBuildOutcome(
        string Layer,
        List<ContextEntry> Entries,
        long ElapsedMilliseconds,
        bool IsSkipped,
        bool IsFaulted)
    {
        public static ContextLayerBuildOutcome Succeeded(string layer, List<ContextEntry> entries, long elapsedMilliseconds)
            => new(layer, entries, elapsedMilliseconds, IsSkipped: false, IsFaulted: false);

        public static ContextLayerBuildOutcome Skipped(string layer)
            => new(layer, new List<ContextEntry>(), 0, IsSkipped: true, IsFaulted: false);

        public static ContextLayerBuildOutcome Failed(string layer, long elapsedMilliseconds)
            => new(layer, new List<ContextEntry>(), elapsedMilliseconds, IsSkipped: false, IsFaulted: true);
    }

    internal sealed record AsyncContextLayerBatch(
        ContextLayerBuildOutcome L0,
        ContextLayerBuildOutcome L1,
        ContextLayerBuildOutcome L2,
        ContextLayerBuildOutcome L3,
        ContextLayerBuildOutcome L5);
}
