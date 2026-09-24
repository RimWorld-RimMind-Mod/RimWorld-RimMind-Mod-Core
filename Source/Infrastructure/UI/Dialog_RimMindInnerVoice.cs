using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Events;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Dialog_RimMindInnerVoice : Window
    {
        private readonly RuntimeServiceRef<IAgentBus> _agentBus = RuntimeServiceRef<IAgentBus>.Optional();
        private readonly Pawn _pawn;
        private readonly AgentIdentity _identity;
        private string _inputText = "";

        public override Vector2 InitialSize => new Vector2(400f, 200f);

        public Dialog_RimMindInnerVoice(Pawn pawn, AgentIdentity identity)
        {
            _pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            forcePause = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var labelRect = new Rect(0f, 0f, inRect.width, 30f);
            Widgets.Label(labelRect, "RimMind.InnerVoice.DialogTitle".Translate(_pawn.LabelShort));

            var inputRect = new Rect(0f, 40f, inRect.width, 30f);
            _inputText = Widgets.TextField(inputRect, _inputText);

            var confirmRect = new Rect(0f, inRect.height - 35f, inRect.width / 2f - 5f, 30f);
            if (Widgets.ButtonText(confirmRect, "Confirm".Translate()))
            {
                SubmitInnerVoice();
                Close();
            }

            var cancelRect = new Rect(inRect.width / 2f + 5f, inRect.height - 35f, inRect.width / 2f - 5f, 30f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }

        private void SubmitInnerVoice()
        {
            if (string.IsNullOrWhiteSpace(_inputText)) return;

            var agentBus = _agentBus.ValueOrDefault;
            if (agentBus == null)
            {
                Log.Warning("[RimMind] InnerVoice: IAgentBus not available");
                return;
            }

            var currentTick = Find.TickManager?.TicksGame ?? 0;
            var expiryTick = currentTick + RimMindDefaults.ProactiveTickInterval; // 1 day

            var evt = new InnerVoiceEvent(
                _identity.NpcId,
                _pawn.thingIDNumber,
                _inputText.Trim(),
                expiryTick,
                currentTick);

            agentBus.Publish(evt);
            Log.Message($"[RimMind] InnerVoice injected for {_identity.NpcId}: {_inputText.Trim()}");
        }
    }
}
