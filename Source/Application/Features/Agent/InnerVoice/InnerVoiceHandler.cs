using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.Events;

namespace RimMind.Application.Features.Agent.InnerVoice
{
    public sealed class InnerVoiceHandler
    {
        private readonly IAgentBus _agentBus;
        private readonly ITickProvider _tickProvider;
        private readonly ILogSink? _log;
        private readonly ConcurrentDictionary<string, PendingVoice> _pendingVoices
            = new ConcurrentDictionary<string, PendingVoice>();
        private string? _subscriptionKey;

        public InnerVoiceHandler(IAgentBus agentBus, ITickProvider tickProvider, ILogSink? log = null)
        {
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
            _log = log;
        }

        public void StartListening()
        {
            _subscriptionKey = _agentBus.Subscribe<InnerVoiceEvent>(OnInnerVoiceEvent);
        }

        public void StopListening()
        {
            if (_subscriptionKey != null)
            {
                _agentBus.Unsubscribe<InnerVoiceEvent>(_subscriptionKey);
                _subscriptionKey = null;
            }
        }

        private void OnInnerVoiceEvent(InnerVoiceEvent evt)
        {
            if (string.IsNullOrWhiteSpace(evt.VoiceText)) return;
            if (string.IsNullOrWhiteSpace(evt.NpcId)) return;

            var pending = new PendingVoice(evt.VoiceText, evt.ExpiryTick);
            _pendingVoices[evt.NpcId] = pending;
            _log?.Message($"[InnerVoice] Received for {evt.NpcId}: {evt.VoiceText}");
        }

        public string? GetPendingVoiceText(string npcId)
        {
            var currentTick = _tickProvider.TicksGame;

            if (_pendingVoices.TryGetValue(npcId, out var pending))
            {
                if (currentTick <= pending.ExpiryTick)
                {
                    return pending.VoiceText;
                }
                // Expired, remove it
                _pendingVoices.TryRemove(npcId, out _);
            }
            return null;
        }

        public void ClearVoice(string npcId)
        {
            _pendingVoices.TryRemove(npcId, out _);
        }

        private sealed class PendingVoice
        {
            public string VoiceText;
            public int ExpiryTick;
            public PendingVoice(string voiceText, int expiryTick)
            {
                VoiceText = voiceText;
                ExpiryTick = expiryTick;
            }
        }
    }
}
