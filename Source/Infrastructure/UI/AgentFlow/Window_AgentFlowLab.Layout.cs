using RimMind.Infrastructure.UI.AgentFlow;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public partial class Window_AgentFlowLab
    {
        private float CalcTotalContentHeight()
        {
            float h = LineH + Padding;
            h += BtnHeight + LineH + Padding + SectionGap;
            h += LineH + BtnHeight + LineH * 2f + Padding * 2f + SectionGap;
            h += LineH + BtnHeight + Padding + SectionGap;
            h += LineH + LineH + BtnHeight + Padding + SectionGap;
            h += LineH + BtnHeight + LineH + LineH + Padding + SectionGap;
            h += LineH + BtnHeight + LineH + LineH + Padding + SectionGap;
            h += LineH + LineH + LineH + Padding + SectionGap;
            h += LineH + BtnHeight + LineH + BtnHeight + LineH + LineH + Padding + SectionGap;
            h += LineH + LineH + Padding + SectionGap;
            h += LineH + BtnHeight * 4 + Padding + SectionGap;
            h += LineH + BtnHeight + Padding;
            return h + Padding * 4;
        }

        private void DrawSectionHeader(ref float y, float w, string key)
        {
            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, w, LineH + 4f), key.Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += LineH + Padding;
        }

        private void DrawStepHeader(ref float y, float w, string key, FlowLabStep step)
        {
            var symbol = StepStatusSymbol(step);
            var color = StepStatusColor(step);
            GUI.color = color;
            Text.Font = GameFont.Small;
            string headerText = $"{symbol} {key.Translate()}";
            Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH), headerText);
            GUI.color = Color.white;
            y += LineH;
        }

        private void DrawSectionLabel(ref float y, float w, string key)
        {
            GUI.color = new Color(0.6f, 0.75f, 1f);
            Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH), key.Translate());
            GUI.color = Color.white;
            y += LineH;
        }

        private void DrawLabel(ref float y, float w, string text, GameFont font)
        {
            Text.Font = font;
            Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH), text);
            Text.Font = GameFont.Small;
            y += LineH;
        }

        private void DrawOfflineModeToggle(ref float y, float w)
        {
            string modeLabel = _offlineMode
                ? "RimMind.UI.AgentFlowLab.OfflineMode".Translate()
                : "RimMind.UI.AgentFlowLab.LiveMode".Translate();

            GUI.color = _offlineMode ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.6f, 0.4f);
            Rect toggleBtn = new Rect(Padding, y, 200f, BtnHeight);
            if (Widgets.ButtonText(toggleBtn, modeLabel))
            {
                _offlineMode = !_offlineMode;
            }
            GUI.color = Color.white;

            string modeHint = _offlineMode
                ? "RimMind.UI.AgentFlowLab.OfflineHint".Translate()
                : "RimMind.UI.AgentFlowLab.LiveHint".Translate();
            GUI.color = Color.grey;
            Widgets.Label(new Rect(210f, y, w - 210f - Padding, BtnHeight), modeHint);
            GUI.color = Color.white;
            y += BtnHeight + LineH + Padding;

            y += SectionGap;
        }

        private Color StepStatusColor(FlowLabStep step)
        {
            StepStatus status = _stepTracker.Get(step);
            return status switch
            {
                StepStatus.Completed => new Color(0.4f, 1f, 0.4f),
                StepStatus.Active => new Color(1f, 1f, 0.4f),
                StepStatus.Failed => new Color(1f, 0.4f, 0.4f),
                _ => new Color(0.5f, 0.5f, 0.5f)
            };
        }

        private string StepStatusSymbol(FlowLabStep step)
        {
            StepStatus status = _stepTracker.Get(step);
            return status switch
            {
                StepStatus.Completed => "\u2713",
                StepStatus.Active => "\u25B6",
                StepStatus.Failed => "\u2717",
                _ => "\u25CB"
            };
        }
    }
}
