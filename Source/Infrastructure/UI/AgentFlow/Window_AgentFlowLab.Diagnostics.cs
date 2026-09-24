using System;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public partial class Window_AgentFlowLab
    {
        private void DrawQueueState(ref float y, float w)
        {
            DrawSectionLabel(ref y, w, "RimMind.UI.AgentFlowLab.QueueState");

            try
            {
                var queue = RuntimeServiceHub.Shared.Capture().GetOptional<IRequestQueue>();
                if (queue != null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Paused: {queue.IsPaused}  Active: {queue.ActiveRequestCount}  LocalBusy: {queue.IsLocalModelBusy}");

                    if (_selectedPawn != null)
                    {
                        string npcId = $"NPC-{_selectedPawn.thingIDNumber}";
                        var active = queue.GetActiveRequests();
                        var pawnRequests = active.Where(r => r.Envelope?.NpcId == npcId).ToList();
                        if (pawnRequests.Count > 0)
                        {
                            sb.AppendLine($"Requests for this pawn ({pawnRequests.Count}):");
                            foreach (var req in pawnRequests)
                                sb.AppendLine($"  {req.RequestId} state={req.State} attempt={req.AttemptCount}");
                        }
                    }

                    _queueInfo = sb.ToString();
                }
                else
                {
                    _queueInfo = "Queue not available";
                }
            }
            catch (Exception ex)
            {
                _queueInfo = $"Error: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(_queueInfo))
            {
                float h = Text.CalcHeight(_queueInfo, w - Padding * 2);
                h = Mathf.Min(h, 60f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _queueInfo);
                GUI.color = Color.white;
                y += h + Padding;
            }

            y += SectionGap;
        }

        private void DrawOpenLogs(ref float y, float w)
        {
            DrawSectionLabel(ref y, w, "RimMind.UI.AgentFlowLab.OpenLogs");

            float btnW = (w - Padding * 6) / 5f;
            float x = Padding;

            if (Widgets.ButtonText(new Rect(x, y, btnW, BtnHeight),
                "RimMind.UI.AgentFlowLab.OpenRequestLog".Translate()))
            {
                Find.WindowStack.Add(new Window_RequestLog());
            }
            x += btnW + Padding;

            if (Widgets.ButtonText(new Rect(x, y, btnW, BtnHeight),
                "RimMind.UI.AgentFlowLab.OpenToolCallDebug".Translate()))
            {
                Find.WindowStack.Add(new Window_ToolCallDebug());
            }
            x += btnW + Padding;

            if (Widgets.ButtonText(new Rect(x, y, btnW, BtnHeight),
                "RimMind.UI.AgentFlowLab.OpenMechanismStatus".Translate()))
            {
                Find.WindowStack.Add(new Window_MechanismStatus());
            }
            x += btnW + Padding;

            if (Widgets.ButtonText(new Rect(x, y, btnW, BtnHeight),
                "RimMind.UI.AgentFlowLab.OpenContextKeys".Translate()))
            {
                Find.WindowStack.Add(new Window_ContextKeyDebug());
            }
            x += btnW + Padding;

            if (Widgets.ButtonText(new Rect(x, y, btnW, BtnHeight),
                "RimMind.UI.AgentFlowLab.OpenAgentProgress".Translate()))
            {
                Find.WindowStack.Add(new Window_AgentProgressFloat());
            }
            y += BtnHeight + Padding;

            y += SectionGap;
        }

        private void DrawErrorLog(ref float y, float w)
        {
            DrawSectionLabel(ref y, w, "RimMind.UI.AgentFlowLab.ErrorLog");

            if (!string.IsNullOrEmpty(_lastError))
            {
                float h = Text.CalcHeight(_lastError, w - Padding * 2 - 80f);
                h = Mathf.Min(h, 40f);
                GUI.color = new Color(1f, 0.5f, 0.4f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2 - 80f, h), _lastError);
                GUI.color = Color.white;
                y += h + Padding;
            }
            else
            {
                GUI.color = Color.grey;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2 - 80f, LineH),
                    "RimMind.UI.AgentFlowLab.NoError".Translate());
                GUI.color = Color.white;
                y += LineH;
            }

            Rect clearBtn = new Rect(w - 80f, y - BtnHeight - Padding, 74f, BtnHeight);
            if (Widgets.ButtonText(clearBtn, "RimMind.UI.AgentFlowLab.ClearError".Translate()))
            {
                _lastError = "";
            }
        }
    }
}
