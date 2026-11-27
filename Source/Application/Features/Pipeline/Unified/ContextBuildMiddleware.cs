using System;
using System.Collections.Generic;
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
        private const string FallbackVisualUserPrompt = "[Attached visual inspection view]";

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
                ApplyAttachments(envelope);
                await next(context);
                return;
            }

            // Build context via IContextEngine if available and NPC mode
            if (_contextEngine == null || envelope.NpcId is not { Length: > 0 } npcId)
            {
                _log?.Message("[UnifiedContextBuild] No context engine or NpcId, skipping context build");
                ApplyAttachments(envelope);
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
                skipLayers,
                envelope.Hints,
                context.Ct);

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
            ApplyAttachments(envelope);

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

        private static void ApplyAttachments(LlmRequestEnvelope envelope)
        {
            var pending = envelope.Attachments;
            if (pending == null)
                return;

            envelope.Attachments = null;

            if (pending.Count == 0 || envelope.Messages == null)
                return;

            var valid = new List<LlmImageAttachment>(pending.Count);
            for (int i = 0; i < pending.Count; i++)
            {
                var att = pending[i];
                if (att != null && !string.IsNullOrWhiteSpace(att.Base64Data))
                {
                    valid.Add(att);
                }
            }

            if (valid.Count == 0)
                return;

            // Attach strictly onto the final L4 user turn (never L0/L1/L2 system prefixes).
            int lastUserIndex = -1;
            for (int i = envelope.Messages.Count - 1; i >= 0; i--)
            {
                var msg = envelope.Messages[i];
                if (msg != null && string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    lastUserIndex = i;
                    break;
                }
            }

            if (lastUserIndex >= 0)
            {
                var original = envelope.Messages[lastUserIndex];
                var combined = original.Attachments != null && original.Attachments.Count > 0
                    ? new List<LlmImageAttachment>(original.Attachments.Count + valid.Count)
                    : new List<LlmImageAttachment>(valid.Count);
                if (original.Attachments != null && original.Attachments.Count > 0)
                    combined.AddRange(original.Attachments);
                combined.AddRange(valid);

                // Clone target ChatMessage so cached/shared snapshot message instances are never mutated.
                envelope.Messages[lastUserIndex] = new ChatMessage
                {
                    Role = original.Role,
                    Content = original.Content,
                    ReasoningContent = original.ReasoningContent,
                    ToolCallId = original.ToolCallId,
                    ToolCalls = original.ToolCalls,
                    LayerTag = original.LayerTag,
                    Attachments = combined
                };
            }
            else
            {
                envelope.Messages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = FallbackVisualUserPrompt,
                    LayerTag = "L4",
                    Attachments = valid
                });
            }
        }
    }
}
