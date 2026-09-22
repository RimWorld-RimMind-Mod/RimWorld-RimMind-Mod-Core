using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static class PromptsTabDrawer
    {
        private enum PromptViewMode { All, Pawn, Map }

        private static Vector2 _promptsScroll;
        private static string _pawnPromptBuffer = string.Empty;
        private static string _mapPromptBuffer = string.Empty;
        private static bool _initialized;
        private static string _lastSyncedPawn = string.Empty;
        private static string _lastSyncedMap = string.Empty;
        private static PromptViewMode _viewMode = PromptViewMode.All;

        public static void ResetBuffer(ISettingsProvider s)
        {
            _pawnPromptBuffer = s.CustomPawnPrompt ?? string.Empty;
            _mapPromptBuffer = s.CustomMapPrompt ?? string.Empty;
            _lastSyncedPawn = _pawnPromptBuffer;
            _lastSyncedMap = _mapPromptBuffer;
            _initialized = true;
        }

        public static void Draw(Rect inRect, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            if (!_initialized)
            {
                ResetBuffer(s);
            }
            else
            {
                if (s.CustomPawnPrompt != _lastSyncedPawn && GUI.GetNameOfFocusedControl() != "RimMind_CustomPawnPrompt")
                {
                    _pawnPromptBuffer = s.CustomPawnPrompt ?? string.Empty;
                    _lastSyncedPawn = _pawnPromptBuffer;
                }
                if (s.CustomMapPrompt != _lastSyncedMap && GUI.GetNameOfFocusedControl() != "RimMind_CustomMapPrompt")
                {
                    _mapPromptBuffer = s.CustomMapPrompt ?? string.Empty;
                    _lastSyncedMap = _mapPromptBuffer;
                }
            }

            float totalContentHeight = _viewMode == PromptViewMode.All ? 480f : 420f;
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

            DrawSelectorBar(listing);

            listing.Gap(8f);

            float editHeight = _viewMode == PromptViewMode.All ? 120f : 240f;

            if (_viewMode == PromptViewMode.All || _viewMode == PromptViewMode.Pawn)
            {
                GUI.SetNextControlName("RimMind_CustomPawnPrompt");
                SettingsUIDrawer.DrawCustomPromptSection(
                    listing,
                    "RimMind.Prompts.PawnPromptLabel".Translate(),
                    ref _pawnPromptBuffer,
                    editHeight,
                    "RimMind.Prompts.Desc".Translate());
                if (_pawnPromptBuffer != s.CustomPawnPrompt)
                {
                    s.CustomPawnPrompt = _pawnPromptBuffer;
                    _lastSyncedPawn = _pawnPromptBuffer;
                    s.Persist();
                }

                Rect pawnBtnRow = listing.GetRect(28f);
                float btnW = 140f;
                Rect resetPawnBtn = new Rect(pawnBtnRow.x, pawnBtnRow.y, btnW, pawnBtnRow.height);
                if (Widgets.ButtonText(resetPawnBtn, "RimMind.UI.ResetToDefaults".Translate()))
                {
                    _pawnPromptBuffer = string.Empty;
                    s.CustomPawnPrompt = string.Empty;
                    _lastSyncedPawn = string.Empty;
                    s.Persist();
                }
                TooltipHandler.TipRegion(resetPawnBtn, "RimMind.Prompts.ResetPawn.Desc".Translate());

                listing.Gap(12f);
            }

            if (_viewMode == PromptViewMode.All || _viewMode == PromptViewMode.Map)
            {
                GUI.SetNextControlName("RimMind_CustomMapPrompt");
                SettingsUIDrawer.DrawCustomPromptSection(
                    listing,
                    "RimMind.Prompts.MapPromptLabel".Translate(),
                    ref _mapPromptBuffer,
                    editHeight,
                    "RimMind.Prompts.Desc".Translate());
                if (_mapPromptBuffer != s.CustomMapPrompt)
                {
                    s.CustomMapPrompt = _mapPromptBuffer;
                    _lastSyncedMap = _mapPromptBuffer;
                    s.Persist();
                }

                Rect mapBtnRow = listing.GetRect(28f);
                float btnW = 140f;
                Rect resetMapBtn = new Rect(mapBtnRow.x, mapBtnRow.y, btnW, mapBtnRow.height);
                if (Widgets.ButtonText(resetMapBtn, "RimMind.UI.ResetToDefaults".Translate()))
                {
                    _mapPromptBuffer = string.Empty;
                    s.CustomMapPrompt = string.Empty;
                    _lastSyncedMap = string.Empty;
                    s.Persist();
                }
                TooltipHandler.TipRegion(resetMapBtn, "RimMind.Prompts.ResetMap.Desc".Translate());
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawSelectorBar(Listing_Standard listing)
        {
            Rect barRect = listing.GetRect(30f);
            float gap = 6f;
            float btnW = (barRect.width - gap * 2f) / 3f;

            Rect btnAll = new Rect(barRect.x, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnAll, PromptViewMode.All, "RimMind.Prompts.ShowAll".Translate(), "RimMind.Prompts.ShowAll.Desc".Translate());

            Rect btnPawn = new Rect(btnAll.xMax + gap, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnPawn, PromptViewMode.Pawn, "RimMind.Prompts.PawnPrompt".Translate(), "RimMind.Prompts.PawnPromptLabel".Translate());

            Rect btnMap = new Rect(btnPawn.xMax + gap, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnMap, PromptViewMode.Map, "RimMind.Prompts.MapPrompt".Translate(), "RimMind.Prompts.MapPromptLabel".Translate());
        }

        private static void DrawViewModeButton(Rect rect, PromptViewMode mode, string label, string tooltip)
        {
            bool active = _viewMode == mode;
            if (active)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.4f, 0.6f, 0.85f));
            }
            if (Widgets.ButtonText(rect, label, drawBackground: !active))
            {
                _viewMode = mode;
            }
            if (active)
            {
                GUI.color = new Color(0.4f, 0.7f, 1f);
                Widgets.DrawBox(rect, 2);
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(rect, tooltip);
        }
    }
}
