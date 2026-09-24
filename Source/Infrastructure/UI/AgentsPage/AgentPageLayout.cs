using RimMind.Presentation.UI.Framework;
using UnityEngine;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public readonly struct AgentPageRects
    {
        public AgentPageRects(
            Rect list,
            Rect detail,
            Rect status,
            Rect actions,
            Rect activity,
            Rect chat,
            ActionBarLayoutResult actionBar)
        {
            List = list;
            Detail = detail;
            Status = status;
            Actions = actions;
            Activity = activity;
            Chat = chat;
            ActionBar = actionBar;
        }

        public Rect List { get; }
        public Rect Detail { get; }
        public Rect Status { get; }
        public Rect Actions { get; }
        public Rect Activity { get; }
        public Rect Chat { get; }
        public ActionBarLayoutResult ActionBar { get; }
    }

    public static class AgentPageLayout
    {
        private const float MinActivityWidth = 260f;
        private const float MinDetailWidth = 220f;
        private const float MaxDetailWidth = 300f;
        private const float DetailWidthRatio = 0.36f;

        public static AgentPageRects Calculate(Rect rect)
        {
            SplitPageLayoutResult split = SplitPageLayout.Calculate(rect, 0.24f, 180f, 240f, MinActivityWidth + MinDetailWidth + RimMindUiMetrics.SplitGap);
            Rect work = split.Detail.InsetSafe(RimMindUiMetrics.Padding);
            float chatHeight = RimMindUiMetrics.BottomBarHeight;
            Rect chat = new Rect(
                work.x,
                work.yMax - chatHeight,
                work.width,
                chatHeight);
            Rect scrollable = new Rect(
                work.x,
                work.y,
                work.width,
                Mathf.Max(1f, chat.y - work.y - RimMindUiMetrics.SectionGap));
            float detailWidth = Mathf.Clamp(scrollable.width * DetailWidthRatio, MinDetailWidth, MaxDetailWidth);
            detailWidth = Mathf.Min(detailWidth, scrollable.width);

            float gap = detailWidth > 0f && scrollable.width - detailWidth > 0f
                ? Mathf.Min(RimMindUiMetrics.SplitGap, scrollable.width - detailWidth)
                : 0f;
            Rect activity = new Rect(
                scrollable.x,
                scrollable.y,
                Mathf.Max(0f, scrollable.width - detailWidth - gap),
                scrollable.height);
            Rect detail = new Rect(
                activity.xMax + gap,
                scrollable.y,
                detailWidth,
                scrollable.height);
            Rect status = new Rect(detail.x, detail.y, detail.width, 80f);
            Rect actions = new Rect(detail.x, status.yMax + RimMindUiMetrics.Padding, detail.width, 70f);
            ActionBarLayoutResult actionBar = ActionBarLayout.Calculate(
                actions,
                new[] { "primary", "force_think", "open_requests" });

            Rect list = new Rect(split.List.x, split.List.y, split.List.width, scrollable.yMax - split.List.y);
            return new AgentPageRects(list, detail, status, actions, activity, chat, actionBar);
        }
    }
}
