using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Events;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Llm
{
    public class ResponseDispatcher : IResponseDispatcher
    {
        private readonly IAgentBus _eventBus;

        public ResponseDispatcher(IAgentBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public void DispatchChatResponse(string npcId, string requestId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            int pawnId = 0;
            var idSpan = npcId.AsSpan();
            if (idSpan.StartsWith("NPC-") && int.TryParse(idSpan.Slice(4), out var pid))
                pawnId = pid;
            _eventBus.Publish(new ActionEvent(
                npcId,
                pawnId,
                "chat_response",
                true,
                "",
                requestId));
        }

        public void Dispatch(LlmRequestContext context, LlmResponse response)
        {
            if (context == null || response == null) return;

            var npcId = context.Envelope?.NpcId;
            DispatchChatResponse(npcId ?? "", response.RequestId);
        }
    }
}
