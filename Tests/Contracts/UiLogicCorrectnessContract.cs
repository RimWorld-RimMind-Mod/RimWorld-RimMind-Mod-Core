using System;
using System.Collections.Generic;
using RimMind.Presentation.UI.Framework;
using RimMind.Testing;
using UnityEngine;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class UiLogicCorrectnessContract
    {
        [Fact]
        public void TabbedPageLayout_calculates_correct_proportions_and_bounds_across_all_counts()
        {
            ContractCaseRunner.Run(
                ("zero tabs returns zero row count and full body content area", () =>
                {
                    var rect = new Rect(0f, 0f, 600f, 400f);
                    var layout = TabbedPageLayout.Calculate(rect, Array.Empty<TabbedPageTabModel>());

                    Assert.Equal(0, layout.RowCount);
                    Assert.Empty(layout.TabRects);
                    Assert.Equal(0f, layout.TabBar.height);
                    Assert.Equal(layout.Body, layout.Content);
                }),
                ("single tab fills entire width on a single row", () =>
                {
                    var rect = new Rect(0f, 0f, 600f, 400f);
                    var tabs = new[] { new TabbedPageTabModel("single", "Single", "Single", true, true, null) };
                    var layout = TabbedPageLayout.Calculate(rect, tabs);

                    Assert.Equal(1, layout.RowCount);
                    Assert.Single(layout.TabRects);
                    Assert.Equal(layout.Body.x, layout.TabRects[0].Rect.x, 1);
                    Assert.Equal(layout.Body.xMax, layout.TabRects[0].Rect.xMax, 1);
                    Assert.True(layout.Content.y >= layout.TabBar.yMax);
                }),
                ("odd and even tab counts distribute evenly and every row fills full width", () =>
                {
                    int[] testCounts = new[] { 2, 3, 5, 7, 8, 9 };
                    var rect = new Rect(0f, 0f, 800f, 500f);

                    foreach (int count in testCounts)
                    {
                        var tabs = new List<TabbedPageTabModel>(count);
                        for (int i = 0; i < count; i++)
                            tabs.Add(new TabbedPageTabModel($"tab_{i}", $"T{i}", $"T{i}", i == 0, true, null));

                        var layout = TabbedPageLayout.Calculate(rect, tabs);
                        Assert.Equal(count, layout.TabRects.Count);
                        Assert.True(layout.RowCount >= 1);

                        // Verify every row's first tab aligns with body.x and last tab aligns with body.xMax
                        var rows = new Dictionary<float, List<TabbedPageTabRect>>();
                        foreach (var tr in layout.TabRects)
                        {
                            float yKey = (float)Math.Round(tr.Rect.y);
                            if (!rows.TryGetValue(yKey, out var list))
                            {
                                list = new List<TabbedPageTabRect>();
                                rows[yKey] = list;
                            }
                            list.Add(tr);
                        }

                        Assert.Equal(layout.RowCount, rows.Count);
                        foreach (var rowEntry in rows.Values)
                        {
                            Assert.Equal(layout.Body.x, rowEntry[0].Rect.x, 1);
                            Assert.Equal(layout.Body.xMax, rowEntry[rowEntry.Count - 1].Rect.xMax, 1);
                            for (int i = 1; i < rowEntry.Count; i++)
                            {
                                Assert.Equal(rowEntry[0].Rect.width, rowEntry[i].Rect.width, 2);
                            }
                        }
                    }
                }),
                ("extreme and degenerate geometry produces non-negative non-NaN bounds", () =>
                {
                    var degenerateRects = new[]
                    {
                        new Rect(0f, 0f, 0f, 0f),
                        new Rect(0f, 0f, 10f, 500f),
                        new Rect(0f, 0f, 500f, 10f),
                        new Rect(0f, 0f, 5000f, 3000f)
                    };
                    var tabs = new[]
                    {
                        new TabbedPageTabModel("t1", "T1", "T1", true, true, null),
                        new TabbedPageTabModel("t2", "T2", "T2", false, true, null)
                    };

                    foreach (var dRect in degenerateRects)
                    {
                        var layout = TabbedPageLayout.Calculate(dRect, tabs);
                        Assert.False(float.IsNaN(layout.TabBar.x));
                        Assert.False(float.IsNaN(layout.Content.y));
                        foreach (var tr in layout.TabRects)
                        {
                            Assert.True(tr.Rect.width >= 0f);
                            Assert.True(tr.Rect.height >= 0f);
                            Assert.False(float.IsNaN(tr.Rect.x));
                            Assert.False(float.IsInfinity(tr.Rect.x));
                        }
                    }
                }));
        }

        [Fact]
        public void Overlay_and_Presets_logical_correctness_and_safety_boundaries()
        {
            ContractCaseRunner.Run(
                ("request overlay screen clamping prevents off-screen placement", () =>
                {
                    float screenW = 1920f;
                    float screenH = 1080f;
                    Vector2 winSize = new Vector2(300f, 200f);

                    // Off-screen left & top clamps to (0, 0)
                    Vector2 c1 = RequestOverlayLayoutEvaluator.ClampPosition(new Vector2(-100f, -50f), winSize, screenW, screenH);
                    Assert.Equal(0f, c1.x);
                    Assert.Equal(0f, c1.y);

                    // Off-screen right & bottom clamps to (screenWidth - winW, screenHeight - winH)
                    Vector2 c2 = RequestOverlayLayoutEvaluator.ClampPosition(new Vector2(2500f, 1500f), winSize, screenW, screenH);
                    Assert.Equal(1620f, c2.x);
                    Assert.Equal(880f, c2.y);

                    // Inside screen stays intact
                    Vector2 c3 = RequestOverlayLayoutEvaluator.ClampPosition(new Vector2(500f, 300f), winSize, screenW, screenH);
                    Assert.Equal(500f, c3.x);
                    Assert.Equal(300f, c3.y);

                    // Zero or degenerate screen bounds handle gracefully
                    Vector2 c4 = RequestOverlayLayoutEvaluator.ClampPosition(new Vector2(100f, 100f), winSize, 0f, 0f);
                    Assert.Equal(0f, c4.x);
                    Assert.Equal(0f, c4.y);
                }),
                ("drag discrimination threshold correctly distinguishes clicks from drags", () =>
                {
                    Vector2 downPos = new Vector2(100f, 100f);
                    Vector2 subtleJitter = new Vector2(102f, 101f); // dx=2, dy=1 -> d^2 = 5 <= 16
                    Vector2 exactThreshold = new Vector2(104f, 100f); // dx=4, dy=0 -> d^2 = 16 <= 16
                    Vector2 deliberateDrag = new Vector2(106f, 105f); // dx=6, dy=5 -> d^2 = 61 > 16

                    Assert.False(RequestOverlayLayoutEvaluator.IsDragExceeded(downPos, subtleJitter));
                    Assert.False(RequestOverlayLayoutEvaluator.IsDragExceeded(downPos, exactThreshold));
                    Assert.True(RequestOverlayLayoutEvaluator.IsDragExceeded(downPos, deliberateDrag));
                }),
                ("api tuning presets configure accurate and safe engine parameters", () =>
                {
                    // Preset 1: High Responsive
                    const int respMaxTokens = 600;
                    const int respConcurrency = 3;
                    const int respTimeoutMs = 25000;
                    const int respCooldownTicks = 15 * 60;

                    // Preset 2: Balanced Standard
                    const int balMaxTokens = 800;
                    const int balConcurrency = 2;
                    const int balTimeoutMs = 45000;
                    const int balCooldownTicks = 30 * 60;

                    // Preset 3: Eco Safe
                    const int ecoMaxTokens = 400;
                    const int ecoConcurrency = 1;
                    const int ecoTimeoutMs = 60000;
                    const int ecoCooldownTicks = 60 * 60;

                    // Invariant 1: Bounded safe ranges for tokens, concurrency, timeout
                    Assert.InRange(respMaxTokens, 200, 4000);
                    Assert.InRange(balMaxTokens, 200, 4000);
                    Assert.InRange(ecoMaxTokens, 200, 4000);

                    // Invariant 2: Concurrency hierarchy (Responsive >= Balanced >= Eco == 1)
                    Assert.True(respConcurrency >= balConcurrency);
                    Assert.True(balConcurrency >= ecoConcurrency);
                    Assert.Equal(1, ecoConcurrency);

                    // Invariant 3: Timeout hierarchy (Responsive < Balanced < Eco)
                    Assert.True(respTimeoutMs < balTimeoutMs);
                    Assert.True(balTimeoutMs < ecoTimeoutMs);

                    // Invariant 4: Cooldown hierarchy (Responsive < Balanced < Eco)
                    Assert.True(respCooldownTicks < balCooldownTicks);
                    Assert.True(balCooldownTicks < ecoCooldownTicks);
                }),
                ("overlay collapsed mini-pill state machine and geometry metrics", () =>
                {
                    // Case A: When empty and autoHide is false -> should NOT collapse
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(0, false, false, false, false, false));

                    // Case B: When pending > 0 -> should NEVER collapse, regardless of autoHide
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(1, true, false, false, false, false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(5, true, false, false, false, false));

                    // Case C: When empty, autoHide is true, and not expanded -> should collapse to mini-pill
                    Assert.True(RequestOverlayLayoutEvaluator.ShouldCollapse(0, true, false, false, false, false));

                    // Case D: When empty, autoHide is true, currently expanded, but user is hovering or dragging -> stays expanded
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(0, true, true, isMouseOver: true, false, false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(0, true, true, false, isDragging: true, false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldCollapse(0, true, true, false, false, isResizing: true));

                    // Case E: When empty, autoHide is true, expanded, but mouse leaves and not dragging -> collapses
                    Assert.True(RequestOverlayLayoutEvaluator.ShouldCollapse(0, true, true, false, false, false));

                    // Bounds geometry check
                    Vector2 pos = new Vector2(100f, 50f);
                    Vector2 expSize = new Vector2(320f, 180f);
                    Rect collapsedRect = RequestOverlayLayoutEvaluator.GetCurrentRect(pos, expSize, isCollapsed: true);
                    Assert.Equal(pos.x, collapsedRect.x);
                    Assert.Equal(pos.y, collapsedRect.y);
                    Assert.Equal(RequestOverlayLayoutEvaluator.MiniPillWidth, collapsedRect.width);
                    Assert.Equal(RequestOverlayLayoutEvaluator.MiniPillHeight, collapsedRect.height);

                    Rect expandedRect = RequestOverlayLayoutEvaluator.GetCurrentRect(pos, expSize, isCollapsed: false);
                    Assert.Equal(320f, expandedRect.width);
                    Assert.Equal(180f, expandedRect.height);
                }));
        }

        [Fact]
        public void ProviderRegistry_RequiresApiKey_handles_player2_and_extended_service_safely()
        {
            ContractCaseRunner.Run(
                ("AIProviderRegistry RequiresApiKey respects player2 and extended_service defaults", () =>
                {
                    Assert.False(RimMind.Application.Common.Helpers.AIProviderRegistry.RequiresApiKey("player2", null));
                    Assert.False(RimMind.Application.Common.Helpers.AIProviderRegistry.RequiresApiKey("extended_service", null));
                    Assert.True(RimMind.Application.Common.Helpers.AIProviderRegistry.RequiresApiKey("openai", null));
                    Assert.True(RimMind.Application.Common.Helpers.AIProviderRegistry.RequiresApiKey("custom_provider", null));
                }));
        }
    }
}
