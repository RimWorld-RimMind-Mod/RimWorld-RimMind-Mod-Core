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
            float totalContentHeight = 360f;
            Rect viewRect = new Rect(
                0f,
                0f,
                Mathf.Max(0f, inRect.width - RimMindUiMetrics.ScrollBarWidth),
                Mathf.Max(inRect.height, totalContentHeight));
            Widgets.BeginScrollView(inRect, ref _promptsScroll, viewRect);
            scope?.Record(inRect, "Settings:Prompts:Viewport");
            scope?.Record(viewRect, "Settings:Prompts:Content");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            SettingsUIDrawer.DrawSectionHeader(
                listing,
                "RimMind.Settings.Tab.Prompts".Translate(),
                "RimMind.Prompts.Desc".Translate());

            var prevPawn = s.CustomPawnPrompt;
            var customPawnPrompt = prevPawn;
            SettingsUIDrawer.DrawCustomPromptSection(
                listing,
                "RimMind.Prompts.PawnPromptLabel".Translate(),
                ref customPawnPrompt,
                130f,
                "RimMind.Prompts.Desc".Translate());
            if (customPawnPrompt != prevPawn)
            {
                s.CustomPawnPrompt = customPawnPrompt;
                s.Persist();
            }

            listing.Gap(12f);

            var prevMap = s.CustomMapPrompt;
            var customMapPrompt = prevMap;
            SettingsUIDrawer.DrawCustomPromptSection(
                listing,
                "RimMind.Prompts.MapPromptLabel".Translate(),
                ref customMapPrompt,
                130f,
                "RimMind.Prompts.Desc".Translate());
            if (customMapPrompt != prevMap)
            {
                s.CustomMapPrompt = customMapPrompt;
                s.Persist();
            }

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
