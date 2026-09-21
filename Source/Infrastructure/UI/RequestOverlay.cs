using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Framework;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public static class RequestOverlay
    {
        private static readonly RuntimeServiceRef<IOverlaySettings> OverlaySettings =
            RuntimeServiceRef<IOverlaySettings>.Optional();
        private static readonly RuntimeServiceRef<IWindowService> WindowService =
            RuntimeServiceRef<IWindowService>.Optional();
        private static readonly RuntimeServiceRef<IOverlayService> OverlayService =
            RuntimeServiceRef<IOverlayService>.Optional();

        private static readonly IReadOnlyList<RequestEntry> EmptyPending = Array.Empty<RequestEntry>();
        private static Vector2 _scrollPos = Vector2.zero;
        private static bool _isDragging;
        private static bool _isResizing;
        private static Vector2 _dragStartOffset;
        private static Rect _windowRect;
        private static bool _positionLoaded;
        private static bool _temporarilyClosed;
        private static bool _lastEnabledState;
        private static bool _isExpanded;
        private static Vector2 _dragMouseDownPos;
        private static bool _hasMovedSignificantly;
        private static bool _needsPersistOnInteractionEnd;
        private static string? _cachedPendingCount0;
        private static string? _cachedMiniPillTooltip;
        private static readonly List<float> CachedHeights = new List<float>();

        private static string PendingCount0Label => _cachedPendingCount0 ??= "RimMind.UI.RequestOverlay.PendingCount".Translate(0).ToString();
        private static string MiniPillTooltipLabel => _cachedMiniPillTooltip ??= "RimMind.UI.RequestOverlay.MiniPillTooltip".Translate().ToString();

        private static readonly GenerationUiState GenerationState = new GenerationUiState();

        private const float MiniPillWidth = 120f;
        private const float MiniPillHeight = 24f;
        private const float OptionsBarHeight = 24f;
        private const float ResizeHandleSize = 24f;
        private const float TextPadding = 4f;
        private const float MinWidth = 260f;
        private const float MinHeight = 100f;
        private const float EntryLineH = 22f;
        private const float BtnHeight = 22f;
        private const float BtnPadding = 4f;

        public static void Register(RequestEntry entry)
        {
            RuntimeServiceScope scope = RuntimeServiceHub.Shared.Capture();
            OverlayService.ResolveOptional(scope)?.RegisterPendingRequest(entry);
        }

        public static IReadOnlyList<RequestEntry> Pending
        {
            get
            {
                RuntimeServiceScope scope = RuntimeServiceHub.Shared.Capture();
                return OverlayService.ResolveOptional(scope)?.GetPendingRequests() ?? EmptyPending;
            }
        }

        public static bool Remove(RequestEntry entry)
        {
            RuntimeServiceScope scope = RuntimeServiceHub.Shared.Capture();
            return OverlayService.ResolveOptional(scope)?.TryDismiss(entry) == true;
        }

        public static bool Resolve(RequestEntry entry, string choice)
        {
            RuntimeServiceScope scope = RuntimeServiceHub.Shared.Capture();
            return OverlayService.ResolveOptional(scope)?.TryResolve(entry, choice) == true;
        }

        internal static bool IsCollapsed => !_isExpanded;
        internal static bool IsExpanded => _isExpanded;
        internal static void SetExpandedForTest(bool expanded) => _isExpanded = expanded;
        internal static Rect WindowRect => _windowRect;
        internal static void SetWindowRectForTest(Rect rect) => _windowRect = rect;
        internal static Rect PillRect => new Rect(_windowRect.x, _windowRect.y, MiniPillWidth, MiniPillHeight);

        public static void OnGUI()
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            GenerationUiOperation operation = GenerationUiOperation.Capture(
                RuntimeServiceHub.Shared,
                LifecycleEventSources.RequestOverlay);
            RuntimeServiceScope runtimeScope = operation.Scope;
            var settings = OverlaySettings.ResolveOptional(runtimeScope);
            if (settings == null) return;
            var overlayService = OverlayService.ResolveOptional(runtimeScope);
            var windowService = WindowService.ResolveOptional(runtimeScope);

            if (GenerationState.Refresh(runtimeScope.Generation))
            {
                LoadPositionFromSettings(settings);
                _isDragging = false;
                _isResizing = false;
                _lastEnabledState = settings.RequestOverlayEnabled;
            }

            bool currentlyEnabled = settings.RequestOverlayEnabled;
            if (currentlyEnabled && !_lastEnabledState)
                _temporarilyClosed = false;
            _lastEnabledState = currentlyEnabled;

            if (!currentlyEnabled || _temporarilyClosed) return;

            if (!_positionLoaded)
            {
                _windowRect = new Rect(settings.RequestOverlayX, settings.RequestOverlayY,
                    settings.RequestOverlayW, settings.RequestOverlayH);
                _positionLoaded = true;
            }

            var pending = overlayService?.GetPendingRequests() ?? EmptyPending;
            bool autoHide = settings.RequestOverlayAutoHideWhenEmpty;

            bool shouldCollapse = RequestOverlayLayoutEvaluator.ShouldCollapse(
                pending.Count,
                autoHide,
                _isExpanded,
                Mouse.IsOver(_windowRect),
                _isDragging,
                _isResizing);
            _isExpanded = !shouldCollapse;

            bool isCollapsed = !_isExpanded;
            Rect pillRect = new Rect(_windowRect.x, _windowRect.y, RequestOverlayLayoutEvaluator.MiniPillWidth, RequestOverlayLayoutEvaluator.MiniPillHeight);

            if (!isCollapsed)
            {
                Vector2 clamped = RequestOverlayLayoutEvaluator.ClampPosition(
                    _windowRect.position,
                    _windowRect.size,
                    global::Verse.UI.screenWidth,
                    global::Verse.UI.screenHeight);
                _windowRect.x = clamped.x;
                _windowRect.y = clamped.y;
            }

            HandleInput(isCollapsed, pillRect, settings);

            if (isCollapsed)
            {
                DrawMiniPill(pillRect);
            }
            else
            {
                bool isMouseOver = Mouse.IsOver(_windowRect);
                GUI.BeginGroup(_windowRect);
                try
                {
                    var inRect = new Rect(Vector2.zero, _windowRect.size);

                    Widgets.DrawBoxSolid(inRect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

                    DrawEntries(inRect, pending, overlayService, operation);

                    if (isMouseOver)
                    {
                        DrawOptionsBar(inRect, windowService);

                        var resizeRect = new Rect(inRect.width - ResizeHandleSize, inRect.height - ResizeHandleSize,
                            ResizeHandleSize, ResizeHandleSize);
                        GUI.DrawTexture(resizeRect, TexUI.WinExpandWidget);
                        TooltipHandler.TipRegion(resizeRect, "RimMind.UI.RequestOverlay.DragResize".Translate());
                    }
                }
                finally
                {
                    GUI.EndGroup();
                }
            }
        }

        private static void DrawMiniPill(Rect pillRect)
        {
            GUI.BeginGroup(pillRect);
            Color prevColor = GUI.color;
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                var inPill = new Rect(Vector2.zero, pillRect.size);
                Widgets.DrawBoxSolid(inPill, new Color(0.08f, 0.08f, 0.12f, 0.85f));
                Widgets.DrawHighlightIfMouseover(inPill);
                Widgets.DrawBoxSolid(new Rect(0f, 0f, 3f, inPill.height), new Color(0.4f, 0.7f, 1.0f, 0.7f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.85f);
                Widgets.Label(inPill, PendingCount0Label);
                TooltipHandler.TipRegion(inPill, MiniPillTooltipLabel);
            }
            finally
            {
                GUI.color = prevColor;
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                GUI.EndGroup();
            }
        }

        private static void LoadPositionFromSettings(IOverlaySettings settings)
        {
            _windowRect = new Rect(
                settings.RequestOverlayX,
                settings.RequestOverlayY,
                settings.RequestOverlayW,
                settings.RequestOverlayH);
            _positionLoaded = true;
            GenerationState.MarkDerivedState();
        }

        private static void DrawEntries(
            Rect inRect,
            IReadOnlyList<RequestEntry> pending,
            IOverlayService? overlayService,
            GenerationUiOperation operation)
        {
            var contentRect = inRect.ContractedBy(TextPadding);
            contentRect.yMin += OptionsBarHeight;

            Color prevColor = GUI.color;
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                if (pending.Count == 0)
                {
                    GUI.color = Color.grey;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(contentRect, "RimMind.UI.RequestOverlay.Empty".Translate());
                    return;
                }

                float contentH = 0f;
                CachedHeights.Clear();
                for (int i = 0; i < pending.Count; i++)
                {
                    float h = EntryLineH;
                    if (!pending[i].description.NullOrEmpty())
                        h += EntryLineH;
                    h += BtnHeight + BtnPadding * 2f;
                    CachedHeights.Add(h);
                    contentH += h;
                }

                Rect viewRect = new Rect(contentRect.x, contentRect.y, contentRect.width - 16f, contentH);
                Widgets.BeginScrollView(contentRect, ref _scrollPos, viewRect);
                try
                {
                    float y = viewRect.y;
                    for (int i = 0; i < pending.Count; i++)
                    {
                        var entry = pending[i];
                        float entryH = CachedHeights[i];

                        var entryRect = new Rect(viewRect.x, y, viewRect.width, entryH);
                        Widgets.DrawBoxSolid(entryRect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

                        string header = entry.systemBlocked
                            ? "RimMind.UI.RequestOverlay.SystemBlocked".Translate(entry.title)
                            : entry.pawn is Pawn p
                                ? $"[{p.Name.ToStringShort}] {entry.title}"
                                : entry.title;

                        GUI.color = entry.systemBlocked ? new Color(1f, 0.6f, 0.4f) : new Color(0.85f, 0.9f, 1f);
                        Widgets.Label(new Rect(entryRect.x + TextPadding, entryRect.y + 2f, entryRect.width - TextPadding * 2, EntryLineH), header);
                        GUI.color = Color.white;

                        float descY = entryRect.y + EntryLineH;
                        if (!entry.description.NullOrEmpty())
                        {
                            GUI.color = new Color(0.7f, 0.7f, 0.7f);
                            Widgets.Label(new Rect(entryRect.x + TextPadding, descY, entryRect.width - TextPadding * 2, EntryLineH), entry.description);
                            GUI.color = Color.white;
                            descY += EntryLineH;
                        }

                        float btnY = descY + BtnPadding;
                        float totalBtnW = entryRect.width - TextPadding * 2;
                        float btnW = (totalBtnW - (entry.options.Length - 1) * BtnPadding) / entry.options.Length;
                        for (int j = 0; j < entry.options.Length; j++)
                        {
                            Rect btnRect = new Rect(entryRect.x + TextPadding + j * (btnW + BtnPadding), btnY, btnW, BtnHeight);
                            if (Widgets.ButtonText(btnRect, entry.options[j]))
                            {
                                if (operation.CanPublish())
                                    overlayService?.TryResolve(entry, entry.options[j]);
                                break;
                            }
                            if (entry.optionTooltips != null && j < entry.optionTooltips.Length && !entry.optionTooltips[j].NullOrEmpty())
                                TooltipHandler.TipRegion(btnRect, entry.optionTooltips[j]);
                        }

                        y += entryH;
                    }
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                GUI.color = prevColor;
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
            }
        }

        private static void DrawOptionsBar(Rect inRect, IWindowService? windowService)
        {
            var barRect = new Rect(inRect.x, inRect.y, inRect.width, OptionsBarHeight);
            Widgets.DrawBoxSolid(barRect, new Color(0.06f, 0.06f, 0.09f, 0.9f));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.6f, 0.65f, 0.75f);
            Widgets.Label(new Rect(barRect.x + TextPadding + 2f, barRect.y, barRect.width - 90f, barRect.height),
                "RimMind.UI.RequestOverlay.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            var openBtnRect = new Rect(barRect.xMax - 60f, barRect.y + 2f, 56f, barRect.height - 4f);
            var closeBtnRect = new Rect(barRect.xMax - 82f, barRect.y + 2f, 20f, barRect.height - 4f);
            if (Widgets.ButtonText(closeBtnRect, "X"))
            {
                _temporarilyClosed = true;
            }
            if (Widgets.ButtonText(openBtnRect, "RimMind.UI.RequestOverlay.Details".Translate()))
            {
                windowService?.OpenRequestLog();
            }
        }

        private static void HandleInput(bool isCollapsed, Rect pillRect, IOverlaySettings settings)
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                if (isCollapsed)
                {
                    if (pillRect.Contains(currentEvent.mousePosition))
                    {
                        _isDragging = true;
                        _dragMouseDownPos = currentEvent.mousePosition;
                        _hasMovedSignificantly = false;
                        GenerationState.MarkInteractionActive();
                        _dragStartOffset = currentEvent.mousePosition - _windowRect.position;
                        currentEvent.Use();
                    }
                    return;
                }

                var openBtnScreenRect = new Rect(
                    _windowRect.xMax - 60f, _windowRect.y + 2f, 56f, OptionsBarHeight - 4f);

                var closeBtnScreenRect = new Rect(
                    _windowRect.xMax - 82f, _windowRect.y + 2f, 20f, OptionsBarHeight - 4f);

                var resizeScreenRect = new Rect(
                    _windowRect.xMax - ResizeHandleSize, _windowRect.yMax - ResizeHandleSize,
                    ResizeHandleSize, ResizeHandleSize);

                if (resizeScreenRect.Contains(currentEvent.mousePosition))
                {
                    _isResizing = true;
                    GenerationState.MarkInteractionActive();
                    currentEvent.Use();
                }
                else if (!openBtnScreenRect.Contains(currentEvent.mousePosition)
                    && !closeBtnScreenRect.Contains(currentEvent.mousePosition))
                {
                    var dragRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, OptionsBarHeight);
                    if (dragRect.Contains(currentEvent.mousePosition))
                    {
                        _isDragging = true;
                        GenerationState.MarkInteractionActive();
                        _dragStartOffset = currentEvent.mousePosition - _windowRect.position;
                        currentEvent.Use();
                    }
                }
            }
            else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                bool wasInteracting = _isDragging || _isResizing;
                if (isCollapsed && _isDragging)
                {
                    if (!_hasMovedSignificantly && pillRect.Contains(currentEvent.mousePosition))
                    {
                        _isExpanded = true;
                    }
                }
                _isDragging = false;
                _isResizing = false;
                _hasMovedSignificantly = false;
                GenerationState.ClearInteraction();

                if (wasInteracting && _needsPersistOnInteractionEnd)
                {
                    SavePositionToSettings(settings);
                    _needsPersistOnInteractionEnd = false;
                }

                if (wasInteracting)
                {
                    currentEvent.Use();
                }
            }
            else if (currentEvent.type == EventType.MouseDrag)
            {
                if (_isResizing && !isCollapsed)
                {
                    float desiredWidth = currentEvent.mousePosition.x - _windowRect.x;
                    float desiredHeight = currentEvent.mousePosition.y - _windowRect.y;

                    float maxWidth = global::Verse.UI.screenWidth - _windowRect.x;
                    float maxHeight = global::Verse.UI.screenHeight - _windowRect.y;

                    _windowRect.width = Mathf.Clamp(desiredWidth, MinWidth, maxWidth);
                    _windowRect.height = Mathf.Clamp(desiredHeight, MinHeight, maxHeight);
                    _needsPersistOnInteractionEnd = true;
                    currentEvent.Use();
                }
                else if (_isDragging)
                {
                    if (RequestOverlayLayoutEvaluator.IsDragExceeded(_dragMouseDownPos, currentEvent.mousePosition))
                    {
                        _hasMovedSignificantly = true;
                    }
                    _windowRect.position = currentEvent.mousePosition - _dragStartOffset;
                    float curW = isCollapsed ? RequestOverlayLayoutEvaluator.MiniPillWidth : _windowRect.width;
                    float curH = isCollapsed ? RequestOverlayLayoutEvaluator.MiniPillHeight : _windowRect.height;
                    Vector2 clamped = RequestOverlayLayoutEvaluator.ClampPosition(
                        _windowRect.position,
                        new Vector2(curW, curH),
                        global::Verse.UI.screenWidth,
                        global::Verse.UI.screenHeight);
                    _windowRect.x = clamped.x;
                    _windowRect.y = clamped.y;
                    _needsPersistOnInteractionEnd = true;
                    currentEvent.Use();
                }
            }
        }

        private static void SavePositionToSettings(IOverlaySettings s)
        {
            bool changed = Mathf.Abs(s.RequestOverlayX - _windowRect.x) > 0.1f
                || Mathf.Abs(s.RequestOverlayY - _windowRect.y) > 0.1f
                || Mathf.Abs(s.RequestOverlayW - _windowRect.width) > 0.1f
                || Mathf.Abs(s.RequestOverlayH - _windowRect.height) > 0.1f;

            s.RequestOverlayX = _windowRect.x;
            s.RequestOverlayY = _windowRect.y;
            s.RequestOverlayW = _windowRect.width;
            s.RequestOverlayH = _windowRect.height;

            if (changed)
                s.Persist();
        }
    }
}
