using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class SettingsEntryDebugCenterPageDrawer : IDebugCenterPageDrawer
    {
        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Hub:SettingsEntry");

            float y = rect.y;
            y = RimMindUI.DrawSectionHeader(rect, y, "RimMind.UI.Hub.SettingsEntryTitle".Translate());
            y = RimMindUI.DrawWrappedLabel(
                rect,
                y,
                "RimMind.UI.Hub.SettingsEntryDescription".Translate(),
                RimMindUI.ColorValue,
                scope.Recorder);

            y += RimMindUI.SectionGap;
            Rect buttonRect = new Rect(rect.x + RimMindUI.Padding, y, 180f, RimMindUI.BtnHeight);
            scope.Record(buttonRect, "Hub:SettingsEntry:OpenSettings");
            if (Widgets.ButtonText(buttonRect, "RimMind.UI.Hub.OpenSettings".Translate()))
            {
                OpenSettings();
            }
        }

        private void OpenSettings()
        {
            Find.WindowStack.Add(new Window_RimMindSettings());
        }
    }
}
