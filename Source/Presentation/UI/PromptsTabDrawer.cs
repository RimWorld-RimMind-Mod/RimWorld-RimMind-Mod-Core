using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static class PromptsTabDrawer
    {
        private static Vector2 _promptsScroll;

        public static void Draw(Rect inRect, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 2, rowsPerSection: 3);
            Rect viewRect = new Rect(
                0f,
                0f,
                formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth,
                Mathf.Max(460f, formLayout.ContentHeight));
            Widgets.BeginScrollView(inRect, ref _promptsScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Prompts:Viewport");
            scope?.Record(viewRect, "Settings:Prompts:Content");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            var customPawnPrompt = s.CustomPawnPrompt;
            SettingsUIDrawer.DrawCustomPromptSection(listing,
                "RimMind.Prompts.PawnPromptLabel".Translate(),
                ref customPawnPrompt, 100f,
                "RimMind.Prompts.Desc".Translate());
            s.CustomPawnPrompt = customPawnPrompt;

            listing.Gap(12f);

            var customMapPrompt = s.CustomMapPrompt;
            SettingsUIDrawer.DrawCustomPromptSection(listing,
                "RimMind.Prompts.MapPromptLabel".Translate(),
                ref customMapPrompt, 100f,
                "RimMind.Prompts.Desc".Translate());
            s.CustomMapPrompt = customMapPrompt;

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
