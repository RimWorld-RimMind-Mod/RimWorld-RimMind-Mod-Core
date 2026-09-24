using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class ContextBuildMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedContextBuild";
        public int Order => RimMindDefaults.MiddlewareOrder.ContextBuild;
        public string Id => "UnifiedContextBuild";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly IContextEngine? _contextEngine;
        private readonly ILogSink? _log;

        public ContextBuildMiddleware(IContextEngine? contextEngine = null, ILogSink? log = null)
        {
            _contextEngine = contextEngine;
            _log = log;
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            var envelope = context.Envelope;
            if (envelope == null)
            {
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Null envelope"));
                context.ShortCircuit("NullEnvelope");
                return;
            }

            // If messages are already populated, skip context building
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                _log?.Message($"[UnifiedContextBuild] Messages already populated ({envelope.Messages.Count}), skipping context build");
                ApplySystemAugmentations(envelope);
                await next(context);
                return;
            }

            // Build context via IContextEngine if available and NPC mode
            if (_contextEngine == null || envelope.NpcId is not { Length: > 0 } npcId)
            {
                _log?.Message("[UnifiedContextBuild] No context engine or NpcId, skipping context build");
                await next(context);
                return;
            }

            // Build snapshot directly from envelope fields (no ContextRequest needed)
            var skipLayers = new HashSet<string>();
            if (envelope.GameStateInfo != null
                && envelope.GameStateInfo.ContainsSection("perceptions"))
            {
                skipLayers.Add("L3");
            }

            var snapshot = await _contextEngine.BuildSnapshotFromEnvelopeAsync(
                npcId,
                envelope.GameStateInfo,
                envelope.MaxTokens,
                envelope.Temperature,
                envelope.ScenarioId,
                skipLayers);

            if (snapshot == null)
            {
                _log?.Warning($"[UnifiedContextBuild] Context build returned null for NPC {npcId}");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ContextBuildFailed($"Context build returned null for NPC {npcId}"));
                context.ShortCircuit("context_build_null");
                return;
            }

            context.Snapshot = snapshot;

            // Populate envelope messages from snapshot (both are Domain.Llm.ChatMessage now)
            var messages = envelope.Messages;
            if (messages == null)
            {
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Envelope messages collection is null"));
                context.ShortCircuit("NullMessages");
                return;
            }

            foreach (var msg in snapshot.Messages)
            {
                messages.Add(msg);
            }

            ApplySystemAugmentations(envelope);

            _log?.Message($"[UnifiedContextBuild] Built context for NPC {npcId}: {snapshot.Messages.Count} messages, {snapshot.EstimatedTokens} tokens");

            await next(context);
        }

        private static void ApplySystemAugmentations(LlmRequestEnvelope envelope)
        {
            if (envelope.SystemAugmentations == null)
                return;

            PromptAugmentation.InsertAfterLastSystem(envelope.Messages, envelope.SystemAugmentations);
            envelope.SystemAugmentations = null;
        }

    }
}
