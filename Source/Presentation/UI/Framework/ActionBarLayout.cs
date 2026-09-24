using System.Collections.Generic;
using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public readonly struct ActionButtonRect
    {
        public ActionButtonRect(string id, Rect rect)
        {
            Id = id;
            Rect = rect;
        }

        public string Id { get; }
        public Rect Rect { get; }
    }

    public sealed class ActionBarLayoutResult
    {
        public ActionBarLayoutResult(int rowCount, IReadOnlyList<ActionButtonRect> buttons)
        {
            RowCount = rowCount;
            Buttons = buttons;
        }

        public int RowCount { get; }
        public IReadOnlyList<ActionButtonRect> Buttons { get; }
    }

    public static class ActionBarLayout
    {
        public static ActionBarLayoutResult Calculate(Rect rect, IReadOnlyList<string> buttonIds)
        {
            int count = buttonIds.Count;
            if (count == 0)
                return new ActionBarLayoutResult(0, new List<ActionButtonRect>());

            int perRow = System.Math.Max(1, (int)System.Math.Floor((rect.width + RimMindUiMetrics.ButtonGap) /
                (RimMindUiMetrics.ButtonMinWidth + RimMindUiMetrics.ButtonGap)));
            int rowCount = (int)System.Math.Ceiling((float)count / perRow);
            var buttons = new List<ActionButtonRect>(count);
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int colsInRow = row == rowCount - 1 ? count - row * perRow : perRow;
                float width = Mathf.Max(0f, (rect.width - RimMindUiMetrics.ButtonGap * (colsInRow - 1)) / colsInRow);
                Rect button = new Rect(
                    rect.x + col * (width + RimMindUiMetrics.ButtonGap),
                    rect.y + row * (RimMindUiMetrics.ButtonHeight + RimMindUiMetrics.TabGap),
                    width,
                    RimMindUiMetrics.ButtonHeight);
                buttons.Add(new ActionButtonRect(buttonIds[i], button));
            }
            return new ActionBarLayoutResult(rowCount, buttons);
        }
    }
}
