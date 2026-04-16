using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimMind.Core.UI
{
    public static class RequestOverlay
    {
        private static readonly List<RequestEntry> _pending = new List<RequestEntry>();
        private static Vector2 _scrollPos = Vector2.zero;
        private static bool _isDragging;
        private static bool _isResizing;
        private static Vector2 _dragStartOffset;
        private static Rect _windowRect;
        private static bool _positionLoaded;

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
            entry.tick = Find.TickManager?.TicksGame ?? 0;
            _pending.Add(entry);
        }

        public static IReadOnlyList<RequestEntry> Pending => _pending;

        public static void Remove(RequestEntry entry) => _pending.Remove(entry);

        public static Rect GetWindowRect() => _windowRect;

        public static void SetWindowRect(Rect rect)
        {
            _windowRect = rect;
        }

        public static void OnGUI()
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            var settings = RimMindCoreMod.Settings;
            if (settings == null || !settings.requestOverlayEnabled) return;

            if (!_positionLoaded)
            {
                _windowRect = new Rect(settings.requestOverlayX, settings.requestOverlayY,
                    settings.requestOverlayW, settings.requestOverlayH);
                _positionLoaded = true;
            }

            ProcessExpiredEntries();
            HandleInput();

            bool isMouseOver = Mouse.IsOver(_windowRect);

            GUI.BeginGroup(_windowRect);
            var inRect = new Rect(Vector2.zero, _windowRect.size);

            Widgets.DrawBoxSolid(inRect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

            DrawEntries(inRect);

            if (isMouseOver)
            {
                DrawOptionsBar(inRect);

                var resizeRect = new Rect(inRect.width - ResizeHandleSize, inRect.height - ResizeHandleSize,
                    ResizeHandleSize, ResizeHandleSize);
                GUI.DrawTexture(resizeRect, TexUI.WinExpandWidget);
                TooltipHandler.TipRegion(resizeRect, "RimMind.Core.UI.RequestOverlay.DragResize".Translate());
            }

            GUI.EndGroup();

            SavePositionToSettings();
        }

        private static void DrawEntries(Rect inRect)
        {
            var contentRect = inRect.ContractedBy(TextPadding);
            contentRect.yMin += OptionsBarHeight;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (_pending.Count == 0)
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(contentRect, "RimMind.Core.UI.RequestOverlay.Empty".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float contentH = 0f;
            float[] heights = new float[_pending.Count];
            for (int i = 0; i < _pending.Count; i++)
            {
                float h = EntryLineH;
                if (!_pending[i].description.NullOrEmpty())
                    h += EntryLineH;
                h += BtnHeight + BtnPadding * 2f;
                heights[i] = h;
                contentH += h;
            }

            Rect viewRect = new Rect(contentRect.x, contentRect.y, contentRect.width - 16f, contentH);
            Widgets.BeginScrollView(contentRect, ref _scrollPos, viewRect);

            float y = viewRect.y;
            for (int i = 0; i < _pending.Count; i++)
            {
                var entry = _pending[i];
                float entryH = heights[i];

                var entryRect = new Rect(viewRect.x, y, viewRect.width, entryH);
                Widgets.DrawBoxSolid(entryRect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

                string header = entry.systemBlocked
                    ? "RimMind.Core.UI.RequestOverlay.SystemBlocked".Translate(entry.title)
                    : entry.pawn != null
                        ? $"[{entry.pawn.Name.ToStringShort}] {entry.title}"
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
                        entry.callback?.Invoke(entry.options[j]);
                        _pending.RemoveAt(i);
                        break;
                    }
                }

                y += entryH;
            }

            Widgets.EndScrollView();

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawOptionsBar(Rect inRect)
        {
            var barRect = new Rect(inRect.x, inRect.y, inRect.width, OptionsBarHeight);
            Widgets.DrawBoxSolid(barRect, new Color(0.05f, 0.05f, 0.08f, 0.8f));

            var titleRect = new Rect(barRect.x + 4f, barRect.y, 100f, barRect.height);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.7f, 0.8f, 1f);
            Widgets.Label(titleRect, "RimMind.Core.UI.RequestOverlay.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            var openBtnRect = new Rect(barRect.xMax - 60f, barRect.y + 2f, 56f, barRect.height - 4f);
            if (Widgets.ButtonText(openBtnRect, "RimMind.Core.UI.RequestOverlay.Details".Translate()))
            {
                Find.WindowStack.Add(new Window_RequestLog());
            }
        }

        private static void HandleInput()
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                var openBtnScreenRect = new Rect(
                    _windowRect.xMax - 60f, _windowRect.y + 2f, 56f, OptionsBarHeight - 4f);

                var resizeScreenRect = new Rect(
                    _windowRect.xMax - ResizeHandleSize, _windowRect.yMax - ResizeHandleSize,
                    ResizeHandleSize, ResizeHandleSize);

                if (resizeScreenRect.Contains(currentEvent.mousePosition))
                {
                    _isResizing = true;
                    currentEvent.Use();
                }
                else if (!openBtnScreenRect.Contains(currentEvent.mousePosition))
                {
                    var dragRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, OptionsBarHeight);
                    if (dragRect.Contains(currentEvent.mousePosition))
                    {
                        _isDragging = true;
                        _dragStartOffset = currentEvent.mousePosition - _windowRect.position;
                        currentEvent.Use();
                    }
                }
            }
            else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                _isDragging = false;
                _isResizing = false;
            }
            else if (currentEvent.type == EventType.MouseDrag)
            {
                if (_isResizing)
                {
                    float desiredWidth = currentEvent.mousePosition.x - _windowRect.x;
                    float desiredHeight = currentEvent.mousePosition.y - _windowRect.y;

                    float maxWidth = Verse.UI.screenWidth - _windowRect.x;
                    float maxHeight = Verse.UI.screenHeight - _windowRect.y;

                    _windowRect.width = Mathf.Clamp(desiredWidth, MinWidth, maxWidth);
                    _windowRect.height = Mathf.Clamp(desiredHeight, MinHeight, maxHeight);
                    currentEvent.Use();
                }
                else if (_isDragging)
                {
                    _windowRect.position = currentEvent.mousePosition - _dragStartOffset;
                    _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Verse.UI.screenWidth - _windowRect.width);
                    _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Verse.UI.screenHeight - _windowRect.height);
                    currentEvent.Use();
                }
            }
        }

        private static void ProcessExpiredEntries()
        {
            if (_pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                if (entry.expireTicks <= 0) continue;
                if (now - entry.tick >= entry.expireTicks)
                {
                    string? ignoreOption = entry.options.Length > 0
                        ? entry.options[entry.options.Length - 1]
                        : null;
                    if (ignoreOption != null)
                        entry.callback?.Invoke(ignoreOption);
                    _pending.RemoveAt(i);
                }
            }
        }

        private static void SavePositionToSettings()
        {
            var s = RimMindCoreMod.Settings;
            if (s == null) return;
            s.requestOverlayX = _windowRect.x;
            s.requestOverlayY = _windowRect.y;
            s.requestOverlayW = _windowRect.width;
            s.requestOverlayH = _windowRect.height;
        }
    }
}
