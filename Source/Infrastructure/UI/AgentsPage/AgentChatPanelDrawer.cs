using RimMind.Presentation.UI.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentChatPanelDrawer
    {
        public void Draw(Rect rect, Pawn pawn, ref string chatDraft, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Agents:Chat");

            Rect inputRect = new(rect.x, rect.y, rect.width - 80f, rect.height);
            Rect sendRect = new(rect.xMax - 74f, rect.y, 74f, rect.height);
            scope.Record(inputRect, "Agents:Chat:Input");
            scope.Record(sendRect, "Agents:Chat:Send");

            chatDraft = Widgets.TextField(inputRect, chatDraft);
            if (Widgets.ButtonText(sendRect, "RimMind.UI.AgentsPage.Send".Translate()))
                SendAgentMessage(pawn, chatDraft);
        }

        private static void SendAgentMessage(Pawn pawn, string chatDraft)
        {
            if (string.IsNullOrWhiteSpace(chatDraft)) return;
            Messages.Message("RimMind.UI.AgentsPage.MessageUnavailable".Translate(),
                MessageTypeDefOf.RejectInput, false);
        }
    }
}
