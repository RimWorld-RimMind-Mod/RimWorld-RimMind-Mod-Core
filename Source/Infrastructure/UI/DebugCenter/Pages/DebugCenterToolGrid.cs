using System;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    internal static class DebugCenterToolGrid
    {
        public static void Draw(Rect rect, RimMindLayoutScope? scope, params (string LabelKey, Action Action)[] tools)
        {
            scope?.Record(rect, "ToolGrid");
            float colW = (rect.width - RimMindUI.Padding) / 2f;
            for (int i = 0; i < tools.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect button = new Rect(
                    rect.x + col * (colW + RimMindUI.Padding),
                    rect.y + row * (RimMindUI.BtnHeight + RimMindUI.Padding),
                    colW,
                    RimMindUI.BtnHeight);
                scope?.Record(button, $"ToolGrid:Button:{tools[i].LabelKey}");

                if (Widgets.ButtonText(button, tools[i].LabelKey.Translate()))
                    tools[i].Action();
            }
        }
    }
}
