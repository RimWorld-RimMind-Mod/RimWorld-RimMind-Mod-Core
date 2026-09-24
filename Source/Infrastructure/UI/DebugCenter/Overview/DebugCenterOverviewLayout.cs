using System.Collections.Generic;
using RimMind.Presentation.UI.Framework;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter.Overview
{
    public sealed class DebugCenterOverviewLayoutResult
    {
        public DebugCenterOverviewLayoutResult(
            Rect viewport,
            Rect viewRect,
            IReadOnlyList<Rect> cards,
            Rect summary,
            Rect lifecycleHeader,
            Rect lifecycleRuntime,
            Rect lifecycleGame,
            Rect quickActionsHeader,
            Rect quickActions)
        {
            Viewport = viewport;
            ViewRect = viewRect;
            Cards = cards;
            Summary = summary;
            LifecycleHeader = lifecycleHeader;
            LifecycleRuntime = lifecycleRuntime;
            LifecycleGame = lifecycleGame;
            QuickActionsHeader = quickActionsHeader;
            QuickActions = quickActions;
        }

        public Rect Viewport { get; }
        public Rect ViewRect { get; }
        public IReadOnlyList<Rect> Cards { get; }
        public Rect Summary { get; }
        public Rect LifecycleHeader { get; }
        public Rect LifecycleRuntime { get; }
        public Rect LifecycleGame { get; }
        public Rect QuickActionsHeader { get; }
        public Rect QuickActions { get; }
    }

    public static class DebugCenterOverviewLayout
    {
        private const float CardHeight = 84f;
        private const float SummaryHeight = 104f;
        private const float SectionHeaderHeight = 28f;
        private const float DiagnosticsHeight = 124f;
        private const float QuickActionsHeight = 54f;

        public static DebugCenterOverviewLayoutResult Calculate(Rect viewport)
        {
            float width = Mathf.Max(1f, viewport.width - RimMindUiMetrics.ScrollBarWidth);
            float gap = RimMindUiMetrics.Padding;
            float sectionGap = RimMindUiMetrics.SectionGap;
            float columnWidth = Mathf.Max(0f, (width - gap) / 2f);
            var cards = new[]
            {
                new Rect(0f, 0f, columnWidth, CardHeight),
                new Rect(columnWidth + gap, 0f, columnWidth, CardHeight),
                new Rect(0f, CardHeight + gap, columnWidth, CardHeight),
                new Rect(columnWidth + gap, CardHeight + gap, columnWidth, CardHeight)
            };

            Rect summary = new Rect(0f, cards[3].yMax + sectionGap, width, SummaryHeight);
            Rect lifecycleHeader = new Rect(0f, summary.yMax + sectionGap, width, SectionHeaderHeight);
            float diagnosticsY = lifecycleHeader.yMax + gap;
            Rect runtime = new Rect(0f, diagnosticsY, columnWidth, DiagnosticsHeight);
            Rect game = new Rect(columnWidth + gap, diagnosticsY, columnWidth, DiagnosticsHeight);
            Rect quickHeader = new Rect(0f, runtime.yMax + sectionGap, width, SectionHeaderHeight);
            Rect quick = new Rect(0f, quickHeader.yMax, width, QuickActionsHeight);
            Rect view = new Rect(
                0f,
                0f,
                width,
                Mathf.Max(viewport.height + 1f, Mathf.Max(quick.yMax + RimMindUiMetrics.Padding, 820f)));

            return new DebugCenterOverviewLayoutResult(
                viewport,
                view,
                cards,
                summary,
                lifecycleHeader,
                runtime,
                game,
                quickHeader,
                quick);
        }
    }
}
