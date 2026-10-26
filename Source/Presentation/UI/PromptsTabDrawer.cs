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
        private static float _lastMeasuredHeight;
        private static string _pawnPromptBuffer = string.Empty;
        private static string _mapPromptBuffer = string.Empty;
        private static bool _initialized;
        private static string _lastSyncedPawn = string.Empty;
        private static string _lastSyncedMap = string.Empty;
        private static PromptViewMode _viewMode = PromptViewMode.All;
        // Settings.Write() is a synchronous XML write: defer it until the prompt text area loses focus
        // instead of writing on every keystroke.
        private static bool _persistPending;

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
            if (_persistPending)
            {
                string focused = GUI.GetNameOfFocusedControl();
                if (focused != "RimMind_CustomPawnPrompt" && focused != "RimMind_CustomMapPrompt")
                {
                    _persistPending = false;
                    s.Persist();
                }
            }

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

            float baseContentHeight = _viewMode == PromptViewMode.All ? 480f : 420f;
            float totalContentHeight = Mathf.Max(baseContentHeight, _lastMeasuredHeight);
            Rect viewRect = new Rect(
                0f,
                0f,
                Mathf.Max(0f, inRect.width - RimMindUiMetrics.ScrollBarWidth),
                Mathf.Max(inRect.height, totalContentHeight));
            Widgets.BeginScrollView(inRect, ref _promptsScroll, viewRect);
            scope?.Record(inRect, "Settings:Prompts:Viewport");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            string promptsDesc = UiText.Get("RimMind.Prompts.Desc");
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                UiText.Get("RimMind.Settings.Tab.Prompts"),
                promptsDesc);

            DrawSelectorBar(listing);

            listing.Gap(8f);

            float editHeight = _viewMode == PromptViewMode.All ? 120f : 240f;

            if (_viewMode == PromptViewMode.All || _viewMode == PromptViewMode.Pawn)
            {
                GUI.SetNextControlName("RimMind_CustomPawnPrompt");
                SettingsUIDrawer.DrawCustomPromptSection(
                    listing,
                    UiText.Get("RimMind.Prompts.PawnPromptLabel"),
                    ref _pawnPromptBuffer,
                    editHeight,
                    promptsDesc);
                if (_pawnPromptBuffer != s.CustomPawnPrompt)
                {
                    s.CustomPawnPrompt = _pawnPromptBuffer;
                    _lastSyncedPawn = _pawnPromptBuffer;
                    _persistPending = true;
                }

                Rect pawnBtnRow = listing.GetRect(28f);
                float btnW = 140f;
                Rect resetPawnBtn = new Rect(pawnBtnRow.x, pawnBtnRow.y, btnW, pawnBtnRow.height);
                if (Widgets.ButtonText(resetPawnBtn, UiText.Get("RimMind.UI.ResetToDefaults")))
                {
                    _pawnPromptBuffer = string.Empty;
                    s.CustomPawnPrompt = string.Empty;
                    _lastSyncedPawn = string.Empty;
                    s.Persist();
                }
                TooltipHandler.TipRegion(resetPawnBtn, UiText.Get("RimMind.Prompts.ResetPawn.Desc"));

                listing.Gap(12f);
            }

            if (_viewMode == PromptViewMode.All || _viewMode == PromptViewMode.Map)
            {
                GUI.SetNextControlName("RimMind_CustomMapPrompt");
                SettingsUIDrawer.DrawCustomPromptSection(
                    listing,
                    UiText.Get("RimMind.Prompts.MapPromptLabel"),
                    ref _mapPromptBuffer,
                    editHeight,
                    promptsDesc);
                if (_mapPromptBuffer != s.CustomMapPrompt)
                {
                    s.CustomMapPrompt = _mapPromptBuffer;
                    _lastSyncedMap = _mapPromptBuffer;
                    _persistPending = true;
                }

                Rect mapBtnRow = listing.GetRect(28f);
                float btnW = 140f;
                Rect resetMapBtn = new Rect(mapBtnRow.x, mapBtnRow.y, btnW, mapBtnRow.height);
                if (Widgets.ButtonText(resetMapBtn, UiText.Get("RimMind.UI.ResetToDefaults")))
                {
                    _mapPromptBuffer = string.Empty;
                    s.CustomMapPrompt = string.Empty;
                    _lastSyncedMap = string.Empty;
                    s.Persist();
                }
                TooltipHandler.TipRegion(resetMapBtn, UiText.Get("RimMind.Prompts.ResetMap.Desc"));
            }

            _lastMeasuredHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawSelectorBar(Listing_Standard listing)
        {
            Rect barRect = listing.GetRect(30f);
            float gap = 6f;
            float btnW = (barRect.width - gap * 2f) / 3f;

            Rect btnAll = new Rect(barRect.x, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnAll, PromptViewMode.All, UiText.Get("RimMind.Prompts.ShowAll"), UiText.Get("RimMind.Prompts.ShowAll.Desc"));

            Rect btnPawn = new Rect(btnAll.xMax + gap, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnPawn, PromptViewMode.Pawn, UiText.Get("RimMind.Prompts.PawnPrompt"), UiText.Get("RimMind.Prompts.PawnPromptLabel"));

            Rect btnMap = new Rect(btnPawn.xMax + gap, barRect.y, btnW, barRect.height);
            DrawViewModeButton(btnMap, PromptViewMode.Map, UiText.Get("RimMind.Prompts.MapPrompt"), UiText.Get("RimMind.Prompts.MapPromptLabel"));
        }

        private static void DrawViewModeButton(Rect rect, PromptViewMode mode, string label, string tooltip)
        {
            bool active = _viewMode == mode;
            if (SettingsUIDrawer.DrawSelectableButton(rect, label, active, tooltip))
            {
                _viewMode = mode;
                _lastMeasuredHeight = 0f;
            }
        }
    }
}
