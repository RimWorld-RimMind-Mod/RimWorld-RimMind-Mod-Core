using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public readonly struct TableVisibleRowRange
    {
        public TableVisibleRowRange(int firstIndex, int lastExclusive)
        {
            FirstIndex = firstIndex;
            LastExclusive = lastExclusive;
        }

        public int FirstIndex { get; }
        public int LastExclusive { get; }
    }

    public sealed class TablePageLayoutResult
    {
        public TablePageLayoutResult(Rect toolbar, Rect header, Rect body, Rect bottomBar, Rect viewRect)
        {
            Toolbar = toolbar;
            Header = header;
            Body = body;
            BottomBar = bottomBar;
            ViewRect = viewRect;
        }

        public Rect Toolbar { get; }
        public Rect Header { get; }
        public Rect Body { get; }
        public Rect BottomBar { get; }
        public Rect ViewRect { get; }
    }

    public static class TablePageLayout
    {
        public static TablePageLayoutResult Calculate(Rect rect, int rowCount, int columnCount)
        {
            var split = rect.TakeBottom(RimMindUiMetrics.BottomBarHeight, RimMindUiMetrics.SectionGap);
            Rect top = split.Body;
            Rect bottom = split.Bottom;
            float toolbarHeight = Mathf.Min(RimMindUiMetrics.ButtonHeight, top.height);
            Rect toolbar = new Rect(top.x, top.y, top.width, toolbarHeight);
            float headerGap = Mathf.Min(RimMindUiMetrics.Padding, Mathf.Max(0f, top.yMax - toolbar.yMax));
            float headerY = toolbar.yMax + headerGap;
            float headerHeight = Mathf.Min(RimMindUiMetrics.DebugRowHeight, Mathf.Max(0f, top.yMax - headerY));
            Rect header = new Rect(top.x, headerY, top.width, headerHeight);
            Rect body = new Rect(
                top.x,
                header.yMax,
                top.width,
                Mathf.Max(0f, top.yMax - header.yMax));
            Rect view = new Rect(
                0f,
                0f,
                Mathf.Max(0f, Mathf.Max(rect.width - RimMindUiMetrics.ScrollBarWidth, columnCount * 120f)),
                Mathf.Max(0f, rowCount * RimMindUiMetrics.DebugRowHeight));
            return new TablePageLayoutResult(toolbar, header, body, bottom, view);
        }

        public static Rect CalculateColumnRect(
            float contentWidth,
            int columnIndex,
            int columnCount,
            float y,
            float height,
            float horizontalScroll,
            float padding)
        {
            int safeColumnCount = System.Math.Max(1, columnCount);
            float columnWidth = Mathf.Max(0f, contentWidth) / safeColumnCount;
            return new Rect(
                columnIndex * columnWidth - Mathf.Max(0f, horizontalScroll) + padding,
                y,
                Mathf.Max(0f, columnWidth - padding),
                Mathf.Max(0f, height));
        }

        public static TableVisibleRowRange CalculateVisibleRowRange(
            int rowCount,
            float scrollY,
            float viewportHeight,
            float rowHeight)
        {
            if (rowCount <= 0 || rowHeight <= 0f || viewportHeight <= 0f)
                return new TableVisibleRowRange(0, 0);

            int first = System.Math.Max(
                0,
                System.Math.Min(
                    (int)System.Math.Floor(System.Math.Max(0f, scrollY) / rowHeight),
                    rowCount));
            int last = System.Math.Max(
                first,
                System.Math.Min(
                    (int)System.Math.Ceiling((System.Math.Max(0f, scrollY) + viewportHeight) / rowHeight),
                    rowCount));
            return new TableVisibleRowRange(first, last);
        }
    }
}
