using System;
using System.Collections.Generic;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_RequestLog : RimMindWindowBase
    {
        private static readonly IReadOnlyList<RequestEntry> EmptyPending = Array.Empty<RequestEntry>();
        private Vector2 _scrollPos = Vector2.zero;
        private const float Padding = 6f;
        private const float EntryLineH = 22f;
        private const float BtnHeight = 24f;
        private const float BtnPadding = 4f;

        private sealed record RequestLogOperationContext(
            IOverlayService? Overlay,
            IRequestQueue? Queue,
            IApiCredentialSettings? Credentials,
            IReadOnlyList<RequestEntry> Pending);

        public override Vector2 InitialSize => new Vector2(520f, 460f);

        public Window_RequestLog()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            DrawEmbedded(inRect, scope);
        }

        public void DrawEmbedded(Rect inRect, RimMindLayoutScope? scope = null)
        {
            GenerationFencedOperation<RequestLogOperationContext> operation = CaptureOperation();
            RequestLogOperationContext context = operation.State;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            TablePageLayoutResult table = TablePageLayout.Calculate(inRect, context.Pending.Count, 2);
            Rect headerRect = table.Toolbar;
            Rect contentRect = table.Body;
            Rect bottomRect = table.BottomBar;

            scope?.Record(headerRect, "Header:Title");
            scope?.Record(table.Header, "Header:Table");
            scope?.Record(contentRect, "Content:List");
            scope?.Record(bottomRect, "Bottom:Bar");

            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "RimMind.UI.RequestLog.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            DrawContent(contentRect, operation, scope);
            DrawBottomBar(bottomRect, operation, scope);
        }

        private static GenerationFencedOperation<RequestLogOperationContext> CaptureOperation()
            => GenerationFencedOperation<RequestLogOperationContext>.Capture(
                RuntimeServiceHub.Shared,
                LifecycleEventSources.RequestLog,
                runtimeScope =>
                {
                    IOverlayService? overlay = runtimeScope.GetOptional<IOverlayService>();
                    return new RequestLogOperationContext(
                        overlay,
                        runtimeScope.GetOptional<IRequestQueue>(),
                        runtimeScope.GetOptional<IApiCredentialSettings>(),
                        overlay?.GetPendingRequests() ?? EmptyPending);
                });

        private void DrawContent(
            Rect rect,
            GenerationFencedOperation<RequestLogOperationContext> operation,
            RimMindLayoutScope? scope = null)
        {
            IReadOnlyList<RequestEntry> pending = operation.State.Pending;
            if (pending.Count == 0)
            {
                DrawEmptyState(rect, operation.State);
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
            scope?.Record(rect, "ScrollView:Outer");
            scope?.Record(viewRect, "ScrollView:Content");

            float y = rect.y;
            for (int i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                float entryH = heights[i];

                var entryRect = new Rect(viewRect.x, y, viewRect.width, entryH);
                scope?.Record(entryRect, $"Entry:{i}");
                Widgets.DrawBoxSolid(entryRect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

                string header = entry.systemBlocked
                    ? "RimMind.UI.RequestOverlay.SystemBlocked".Translate(entry.title)
                    : entry.pawn is Pawn p
                        ? $"[{p.Name.ToStringShort}] {entry.title}"
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
                        if (operation.CanPublish())
                            operation.State.Overlay?.TryResolve(entry, entry.options[j]);
                        break;
                    }
                }

                y += entryH;
            }

            Widgets.EndScrollView();
        }

        private void DrawEmptyState(Rect rect, RequestLogOperationContext context)
        {
            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;

            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, centerY - 30f, rect.width, EntryLineH),
                "RimMind.UI.RequestOverlay.Empty".Translate());

            var sb = new StringBuilder();
            var queue = context.Queue;
            if (queue != null && queue.IsPaused)
                sb.AppendLine("RimMind.UI.RequestLog.EmptyReason.QueuePaused".Translate());

            var apiCred = context.Credentials;
            if (apiCred != null && apiCred.ApiKey.NullOrEmpty())
                sb.AppendLine("RimMind.UI.RequestLog.EmptyReason.NoApiKey".Translate());

            bool hasAgent = false;
            var map = Find.CurrentMap;
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    var comp = CompPawnAgent.GetComp(pawn);
                    if (comp?.Agent != null)
                    {
                        hasAgent = true;
                        break;
                    }
                }
            }
            if (!hasAgent)
                sb.AppendLine("RimMind.UI.RequestLog.EmptyReason.NoAgent".Translate());

            if (sb.Length == 0)
                sb.AppendLine("RimMind.UI.RequestLog.EmptyReason.NoRequests".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            float hintH = Text.CalcHeight(sb.ToString().TrimEnd(), rect.width - Padding * 4);
            Widgets.Label(new Rect(rect.x + Padding * 2, centerY, rect.width - Padding * 4, hintH),
                sb.ToString().TrimEnd());
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawBottomBar(
            Rect rect,
            GenerationFencedOperation<RequestLogOperationContext> operation,
            RimMindLayoutScope? scope = null)
        {
            var clearRect = new Rect(rect.xMax - 100f, rect.y, 96f, rect.height - 4f);
            scope?.Record(clearRect, "Button:ClearAll");
            if (Widgets.ButtonText(clearRect, "RimMind.UI.RequestLog.ClearAll".Translate()))
            {
                if (operation.CanPublish())
                    operation.State.Overlay?.Clear();
            }

            var countRect = new Rect(rect.x, rect.y, 200f, rect.height);
            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(countRect, "RimMind.UI.RequestLog.Count".Translate(operation.State.Pending.Count));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}
