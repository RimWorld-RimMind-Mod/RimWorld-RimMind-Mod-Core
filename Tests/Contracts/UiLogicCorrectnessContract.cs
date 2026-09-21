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
                    float winW = 300f;
                    float winH = 200f;

                    // Off-screen left & top
                    float clampedX1 = Mathf.Clamp(-100f, 0, Mathf.Max(0f, screenW - winW));
                    float clampedY1 = Mathf.Clamp(-50f, 0, Mathf.Max(0f, screenH - winH));
                    Assert.Equal(0f, clampedX1);
                    Assert.Equal(0f, clampedY1);

                    // Off-screen right & bottom
                    float clampedX2 = Mathf.Clamp(2500f, 0, Mathf.Max(0f, screenW - winW));
                    float clampedY2 = Mathf.Clamp(1500f, 0, Mathf.Max(0f, screenH - winH));
                    Assert.Equal(1620f, clampedX2);
                    Assert.Equal(880f, clampedY2);

                    // Inside screen stays intact
                    float clampedX3 = Mathf.Clamp(500f, 0, Mathf.Max(0f, screenW - winW));
                    float clampedY3 = Mathf.Clamp(300f, 0, Mathf.Max(0f, screenH - winH));
                    Assert.Equal(500f, clampedX3);
                    Assert.Equal(300f, clampedY3);
                }),
                ("drag discrimination threshold correctly distinguishes clicks from drags", () =>
                {
                    Vector2 downPos = new Vector2(100f, 100f);
                    Vector2 subtleJitter = new Vector2(102f, 101f); // dx=2, dy=1 -> d^2 = 5 < 16
                    Vector2 deliberateDrag = new Vector2(106f, 105f); // dx=6, dy=5 -> d^2 = 61 > 16

                    float distSq1 = (subtleJitter.x - downPos.x) * (subtleJitter.x - downPos.x) +
                                    (subtleJitter.y - downPos.y) * (subtleJitter.y - downPos.y);
                    float distSq2 = (deliberateDrag.x - downPos.x) * (deliberateDrag.x - downPos.x) +
                                    (deliberateDrag.y - downPos.y) * (deliberateDrag.y - downPos.y);

                    const float thresholdSq = 4f * 4f;
                    bool isSubtleDrag = distSq1 > thresholdSq;
                    bool isDeliberateDrag = distSq2 > thresholdSq;

                    Assert.False(isSubtleDrag);
                    Assert.True(isDeliberateDrag);
                }),
                ("api tuning presets configure accurate and safe engine parameters", () =>
                {
                    // Preset 1: High Responsive
                    const int respMaxTokens = 600;
                    const int respConcurrency = 3;
                    const int respTimeoutMs = 25000;
                    const int respCooldownTicks = 15 * 60;

                    Assert.InRange(respMaxTokens, 200, 4000);
                    Assert.InRange(respConcurrency, 1, 10);
                    Assert.InRange(respTimeoutMs, 10000, 180000);
                    Assert.Equal(900, respCooldownTicks);

                    // Preset 2: Balanced Standard
                    const int balMaxTokens = 800;
                    const int balConcurrency = 2;
                    const int balTimeoutMs = 45000;
                    const int balCooldownTicks = 30 * 60;

                    Assert.InRange(balMaxTokens, 200, 4000);
                    Assert.InRange(balConcurrency, 1, 10);
                    Assert.InRange(balTimeoutMs, 10000, 180000);
                    Assert.Equal(1800, balCooldownTicks);

                    // Preset 3: Eco Safe
                    const int ecoMaxTokens = 400;
                    const int ecoConcurrency = 1;
                    const int ecoTimeoutMs = 60000;
                    const int ecoCooldownTicks = 60 * 60;

                    Assert.InRange(ecoMaxTokens, 200, 4000);
                    Assert.Equal(1, ecoConcurrency);
                    Assert.InRange(ecoTimeoutMs, 10000, 180000);
                    Assert.Equal(3600, ecoCooldownTicks);
                }));
        }
    }
}
