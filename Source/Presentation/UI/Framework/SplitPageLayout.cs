using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public sealed class SplitPageLayoutResult
    {
        public SplitPageLayoutResult(Rect root, Rect list, Rect detail)
        {
            Root = root;
            List = list;
            Detail = detail;
        }

        public Rect Root { get; }
        public Rect List { get; }
        public Rect Detail { get; }
    }

    public static class SplitPageLayout
    {
        public static SplitPageLayoutResult Calculate(Rect root, float listRatio, float minList, float maxList, float minDetail)
        {
            float listWidth = Mathf.Clamp(root.width * listRatio, minList, maxList);
            if (root.width - listWidth - RimMindUiMetrics.SplitGap < minDetail)
                listWidth = Mathf.Max(minList, root.width - minDetail - RimMindUiMetrics.SplitGap);

            float maxContainedListWidth = Mathf.Max(0f, root.width - RimMindUiMetrics.SplitGap);
            listWidth = Mathf.Clamp(listWidth, 0f, maxContainedListWidth);
            float gap = listWidth > 0f && root.width - listWidth > 0f
                ? Mathf.Min(RimMindUiMetrics.SplitGap, root.width - listWidth)
                : 0f;
            Rect list = new Rect(root.x, root.y, listWidth, root.height);
            Rect detail = new Rect(
                list.xMax + gap,
                root.y,
                Mathf.Max(0f, root.width - listWidth - gap),
                root.height);
            return new SplitPageLayoutResult(root, list, detail);
        }
    }
}
