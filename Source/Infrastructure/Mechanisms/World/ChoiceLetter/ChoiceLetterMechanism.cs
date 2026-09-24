using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.World.ChoiceLetter
{
    public sealed class ChoiceLetterMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "world.choice_letter";
        public override MechanismScope Scope => MechanismScope.World;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Trigger }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Send notification letters to the player. Full async choice-letter flow (player picks options, AI gets callback) requires a custom ChoiceLetter subclass planned for a future phase.",
            TriggerDescription = "Send a letter to the player. Params: title, description, options (comma-separated). Returns letter_id. Note: currently sends as a notification letter; async player-choice callback is a future enhancement."
        };

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo("notify", "Send a notification letter to the player", requiredParams: new List<string> { "title", "description" }.AsReadOnly()),
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var title = ExtractParam(args, "title") ?? "AI Notification";
            var description = ExtractParam(args, "description") ?? "The AI sends a notification.";

            var letter = LetterMaker.MakeLetter(title, description, LetterDefOf.NeutralEvent);
            Find.LetterStack?.ReceiveLetter(letter);

            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        private static string? ExtractParam(MechanismWriteArgs args, string key)
        {
            if (args.Params != null && args.Params.TryGetValue(key, out var val))
                return val;
            return null;
        }
    }
}
