using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public readonly struct HeaderBodySplit
    {
        public HeaderBodySplit(Rect header, Rect body)
        {
            Header = header;
            Body = body;
        }

        public Rect Header { get; }
        public Rect Body { get; }
    }

    public readonly struct BodyBottomSplit
    {
        public BodyBottomSplit(Rect body, Rect bottom)
        {
            Body = body;
            Bottom = bottom;
        }

        public Rect Body { get; }
        public Rect Bottom { get; }
    }

    public static class RimMindUiRectExtensions
    {
        public static Rect InsetSafe(this Rect rect, float inset)
        {
            float width = Mathf.Max(1f, rect.width - inset * 2f);
            float height = Mathf.Max(1f, rect.height - inset * 2f);
            float x = rect.x + Mathf.Min(inset, rect.width / 2f);
            float y = rect.y + Mathf.Min(inset, rect.height / 2f);
            return new Rect(x, y, width, height);
        }

        public static HeaderBodySplit SplitHeaderBody(this Rect rect, float headerHeight, float gap)
        {
            float h = Mathf.Min(Mathf.Max(0f, headerHeight), rect.height);
            float effectiveGap = Mathf.Min(Mathf.Max(0f, gap), rect.height - h);
            float bodyY = rect.y + h + effectiveGap;
            Rect header = new Rect(rect.x, rect.y, rect.width, h);
            Rect body = new Rect(
                rect.x,
                bodyY,
                rect.width,
                Mathf.Max(0f, rect.yMax - bodyY));
            return new HeaderBodySplit(header, body);
        }

        public static BodyBottomSplit TakeBottom(this Rect rect, float height, float gap)
        {
            float h = Mathf.Min(Mathf.Max(0f, height), rect.height);
            float effectiveGap = Mathf.Min(Mathf.Max(0f, gap), rect.height - h);
            Rect body = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0f, rect.height - h - effectiveGap));
            Rect bottom = new Rect(rect.x, rect.yMax - h, rect.width, h);
            return new BodyBottomSplit(body, bottom);
        }

        public static bool ContainsRect(this Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - RimMindUiMetrics.TextOverflowEpsilon
                && inner.yMin >= outer.yMin - RimMindUiMetrics.TextOverflowEpsilon
                && inner.xMax <= outer.xMax + RimMindUiMetrics.TextOverflowEpsilon
                && inner.yMax <= outer.yMax + RimMindUiMetrics.TextOverflowEpsilon;
        }
    }
}
