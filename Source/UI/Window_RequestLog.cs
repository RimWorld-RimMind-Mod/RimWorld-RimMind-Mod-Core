using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimMind.Core.UI
{
    public class Window_RequestLog : Window
    {
        private Vector2 _scrollPos = Vector2.zero;
        private const float Padding = 6f;
        private const float EntryLineH = 22f;
        private const float BtnHeight = 24f;
        private const float BtnPadding = 4f;

        public override Vector2 InitialSize => new Vector2(520f, 460f);

        public Window_RequestLog()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float headerH = 30f;
            float bottomH = 36f;

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerH);
            Rect contentRect = new Rect(inRect.x, inRect.y + headerH + Padding,
                inRect.width, inRect.height - headerH - bottomH - Padding * 2);
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - bottomH, inRect.width, bottomH);

            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "RimMind.Core.UI.RequestLog.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            DrawContent(contentRect);
            DrawBottomBar(bottomRect);
        }

        private void DrawContent(Rect rect)
        {
            var pending = RequestOverlay.Pending;
            if (pending.Count == 0)
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimMind.Core.UI.RequestOverlay.Empty".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float contentH = 0f;
            float[] heights = new float[pending.Count];
            for (int i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                float h = EntryLineH;
                if (!entry.description.NullOrEmpty())
                    h += Text.CalcHeight(entry.description, rect.width - Padding * 4) + Padding;
                h += BtnHeight + Padding * 3;
                heights[i] = h;
                contentH += h;
            }

            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);

            float y = rect.y;
            for (int i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                float entryH = heights[i];

                var entryRect = new Rect(viewRect.x, y, viewRect.width, entryH);
                Widgets.DrawBoxSolid(entryRect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

                string header = entry.systemBlocked
                    ? "RimMind.Core.UI.RequestOverlay.SystemBlocked".Translate(entry.title)
                    : entry.pawn != null
                        ? $"[{entry.pawn.Name.ToStringShort}] {entry.title}"
                        : entry.title;

                GUI.color = entry.systemBlocked ? new Color(1f, 0.6f, 0.4f) : new Color(0.85f, 0.9f, 1f);
                Widgets.Label(new Rect(entryRect.x + Padding, entryRect.y + Padding,
                    entryRect.width - Padding * 2, EntryLineH), header);
                GUI.color = Color.white;

                float descY = entryRect.y + EntryLineH + Padding;
                if (!entry.description.NullOrEmpty())
                {
                    float descH = Text.CalcHeight(entry.description, entryRect.width - Padding * 4);
                    GUI.color = new Color(0.7f, 0.7f, 0.7f);
                    Widgets.Label(new Rect(entryRect.x + Padding * 2, descY,
                        entryRect.width - Padding * 4, descH), entry.description);
                    GUI.color = Color.white;
                    descY += descH + Padding;
                }

                float btnY = descY + Padding;
                float totalBtnW = entryRect.width - Padding * 2;
                float btnW = (totalBtnW - (entry.options.Length - 1) * BtnPadding) / entry.options.Length;
                for (int j = 0; j < entry.options.Length; j++)
                {
                    Rect btnRect = new Rect(entryRect.x + Padding + j * (btnW + BtnPadding), btnY, btnW, BtnHeight);
                    if (Widgets.ButtonText(btnRect, entry.options[j]))
                    {
                        entry.callback?.Invoke(entry.options[j]);
                        RequestOverlay.Remove(entry);
                        break;
                    }
                }

                y += entryH;
            }

            Widgets.EndScrollView();
        }

        private void DrawBottomBar(Rect rect)
        {
            var clearRect = new Rect(rect.xMax - 100f, rect.y, 96f, rect.height - 4f);
            if (Widgets.ButtonText(clearRect, "RimMind.Core.UI.RequestLog.ClearAll".Translate()))
            {
                var pending = RequestOverlay.Pending.ToList();
                foreach (var entry in pending)
                    RequestOverlay.Remove(entry);
            }

            var countRect = new Rect(rect.x, rect.y, 200f, rect.height);
            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(countRect, "RimMind.Core.UI.RequestLog.Count".Translate(RequestOverlay.Pending.Count));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}
