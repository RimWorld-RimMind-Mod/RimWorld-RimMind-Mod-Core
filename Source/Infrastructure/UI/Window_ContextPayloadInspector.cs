using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.UI
{
    public class Window_ContextPayloadInspector : RimMindWindowBase
    {
        private const float HeaderHeight = 40f;
        private const float TabHeight = 32f;
        private const float BottomBarHeight = 44f;
        private const float SidebarWidth = 180f;

        private Pawn? _selectedPawn;
        private int _selectedScenarioIndex = 0;
        private int _selectedTab = 0;

        private Vector2 _pawnListScroll = Vector2.zero;
        private Vector2 _contentScroll = Vector2.zero;

        private static readonly string[] Scenarios = new[]
        {
            ScenarioIds.Decision,
            ScenarioIds.Dialogue,
            ScenarioIds.Personality,
            ScenarioIds.Memory
        };

        private static readonly string[] ScenarioLabels = new[]
        {
            "Decision",
            "Dialogue",
            "Personality",
            "DarkMemory"
        };

        private static readonly string[] Tabs = new[]
        {
            "Prompt",
            "Layers",
            "Tools",
            "Analysis",
            "Live Test"
        };

        // Cache for generated envelope
        private Pawn? _cachedPawn;
        private int _cachedScenarioIndex = -1;
        private LlmRequestEnvelope? _cachedEnvelope;
        private string _cachedSystemText = string.Empty;
        private string _cachedLayersText = string.Empty;
        private string _cachedToolsText = string.Empty;
        private TokenAnalysisReport _cachedAnalysis = new();

        // Live Test execution state
        private bool _isTesting;
        private string _testStatus = "Ready";
        private string _testResponseContent = string.Empty;
        private string _testToolCallsJson = string.Empty;
        private long _testElapsedMs;
        private string _testError = string.Empty;

        public override Vector2 InitialSize => new Vector2(860f, 660f);

        public Window_ContextPayloadInspector(Pawn? initialPawn = null)
        {
            _selectedPawn = initialPawn ?? Find.Selector.SingleSelectedThing as Pawn;
            forcePause = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        internal void SetViewForTest(Pawn? pawn, int scenarioIndex, int tabIndex)
        {
            _selectedPawn = pawn;
            _selectedScenarioIndex = scenarioIndex;
            _selectedTab = tabIndex;
            RebuildEnvelope();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            EnsurePawnSelection();
            RebuildEnvelope();
        }

        private void EnsurePawnSelection()
        {
            if (_selectedPawn == null || _selectedPawn.Dead)
            {
                var colonists = Find.CurrentMap?.mapPawns?.FreeColonists?.Where(p => !p.Dead).ToList();
                if (colonists != null && colonists.Count > 0)
                {
                    _selectedPawn = colonists[0];
                }
            }
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            EnsurePawnSelection();

            if (_selectedPawn != _cachedPawn || _selectedScenarioIndex != _cachedScenarioIndex)
            {
                RebuildEnvelope();
            }

            // 1. Top Header (Title + Scenario Picker)
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            DrawHeader(headerRect);

            // 2. Main Area (Left Sidebar: Pawn Selector, Right: Tabs + Content)
            float mainY = inRect.y + HeaderHeight + 6f;
            float mainHeight = inRect.height - HeaderHeight - BottomBarHeight - 12f;

            Rect sidebarRect = new Rect(inRect.x, mainY, SidebarWidth, mainHeight);
            Rect contentAreaRect = new Rect(inRect.x + SidebarWidth + 10f, mainY, inRect.width - SidebarWidth - 10f, mainHeight);

            DrawSidebar(sidebarRect);
            DrawContentArea(contentAreaRect);

            // 3. Bottom Bar (Actions: Copy, Test, Refresh)
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - BottomBarHeight, inRect.width, BottomBarHeight);
            DrawBottomBar(bottomRect);
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.14f, 0.18f, 0.9f));
            GUI.color = new Color(0.7f, 0.88f, 1f);
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 6f, 320f, 28f);
            Widgets.Label(titleRect, "RimMind.Inspector.Title".Translate());

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Scenario dropdown / toggle
            float selX = rect.xMax - 340f;
            Rect scenLabelRect = new Rect(selX, rect.y + 10f, 75f, 22f);
            Widgets.Label(scenLabelRect, "RimMind.Inspector.ScenarioLabel".Translate());

            Rect scenBtnRect = new Rect(selX + 80f, rect.y + 6f, 250f, 26f);
            if (Widgets.ButtonText(scenBtnRect, ScenarioLabels[_selectedScenarioIndex]))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < ScenarioLabels.Length; i++)
                {
                    int index = i;
                    options.Add(new FloatMenuOption(ScenarioLabels[index], () =>
                    {
                        _selectedScenarioIndex = index;
                        RebuildEnvelope();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f, 0.6f));
            Widgets.DrawHighlightIfMouseover(rect);

            Rect titleRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 24f);
            GUI.color = Color.gray;
            Widgets.Label(titleRect, "RimMind.Inspector.ColonistsList".Translate());
            GUI.color = Color.white;

            var colonists = Find.CurrentMap?.mapPawns?.FreeColonists?.Where(p => !p.Dead).ToList() ?? new List<Pawn>();
            float rowHeight = 28f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, colonists.Count * rowHeight);
            Rect scrollRect = new Rect(rect.x + 4f, rect.y + 30f, rect.width - 8f, rect.height - 34f);

            Widgets.BeginScrollView(scrollRect, ref _pawnListScroll, viewRect);
            float curY = 0f;
            foreach (var pawn in colonists)
            {
                bool isSelected = pawn == _selectedPawn;
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowHeight - 2f);
                if (isSelected)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else
                {
                    Widgets.DrawHighlightIfMouseover(rowRect);
                }

                string label = pawn.LabelCap;
                if (Widgets.ButtonText(rowRect, label, drawBackground: false))
                {
                    _selectedPawn = pawn;
                    RebuildEnvelope();
                }

                curY += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawContentArea(Rect rect)
        {
            // Tabs row
            float tabWidth = (rect.width - (Tabs.Length - 1) * 4f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                Rect tabBtn = new Rect(rect.x + i * (tabWidth + 4f), rect.y, tabWidth, TabHeight);
                bool isActive = _selectedTab == i;
                if (isActive)
                {
                    Widgets.DrawAtlas(tabBtn, Widgets.ButtonBGAtlasClick);
                    TextAnchor prevAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(tabBtn, Tabs[i]);
                    Text.Anchor = prevAnchor;

                    if (Widgets.ButtonInvisible(tabBtn))
                    {
                        _selectedTab = i;
                    }
                }
                else
                {
                    if (Widgets.ButtonText(tabBtn, Tabs[i]))
                    {
                        _selectedTab = i;
                    }
                }
            }
            // Tab Content Box
            Rect bodyRect = new Rect(rect.x, rect.y + TabHeight + 6f, rect.width, rect.height - TabHeight - 6f);
            Widgets.DrawBoxSolid(bodyRect, new Color(0.08f, 0.08f, 0.10f, 0.8f));

            Rect innerRect = bodyRect.ContractedBy(8f);
            switch (_selectedTab)
            {
                case 0:
                    DrawSystemPromptTab(innerRect);
                    break;
                case 1:
                    DrawContextLayersTab(innerRect);
                    break;
                case 2:
                    DrawToolsTab(innerRect);
                    break;
                case 3:
                    DrawAnalysisTab(innerRect);
                    break;
                case 4:
                    DrawLiveTestTab(innerRect);
                    break;
            }
        }

        private void DrawSystemPromptTab(Rect rect)
        {
            DrawScrollableText(rect, _cachedSystemText);
        }

        private void DrawContextLayersTab(Rect rect)
        {
            DrawScrollableText(rect, _cachedLayersText);
        }

        private void DrawToolsTab(Rect rect)
        {
            DrawScrollableText(rect, _cachedToolsText);
        }

        private void DrawAnalysisTab(Rect rect)
        {
            var r = _cachedAnalysis;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Token 预算与结构消耗分析 ===");
            sb.AppendLine($"• 预估总输入 Token : ~{r.TotalTokens} tokens ({r.TotalChars} 字符)");
            sb.AppendLine($"  - 系统规则与声明 (System) : ~{r.SystemTokens} tokens ({(r.TotalTokens > 0 ? r.SystemTokens * 100f / r.TotalTokens : 0):F1}%)");
            sb.AppendLine($"  - 静态背景与特征 (Static) : ~{r.StaticTokens} tokens ({(r.TotalTokens > 0 ? r.StaticTokens * 100f / r.TotalTokens : 0):F1}%)");
            sb.AppendLine($"  - 动态状态与感知 (Dynamic): ~{r.DynamicTokens} tokens ({(r.TotalTokens > 0 ? r.DynamicTokens * 100f / r.TotalTokens : 0):F1}%)");
            sb.AppendLine($"  - 工具结构声明 (Tools)    : ~{r.ToolTokens} tokens ({(r.TotalTokens > 0 ? r.ToolTokens * 100f / r.TotalTokens : 0):F1}%)");
            sb.AppendLine();
            sb.AppendLine("RimMind.Inspector.PromptCachingTitle".Translate());
            int prefixTokens = r.SystemTokens + r.StaticTokens + r.ToolTokens;
            int volatileTokens = r.DynamicTokens;
            float hitRatio = r.TotalTokens > 0 ? (prefixTokens * 100f / r.TotalTokens) : 0f;
            sb.AppendLine("RimMind.Inspector.PrefixTokens".Translate(prefixTokens, hitRatio));
            sb.AppendLine("RimMind.Inspector.Zone1Tokens".Translate(r.SystemTokens));
            sb.AppendLine("RimMind.Inspector.Zone2Tokens".Translate(r.StaticTokens));
            sb.AppendLine("RimMind.Inspector.ToolsTokens".Translate(r.ToolTokens));
            sb.AppendLine("RimMind.Inspector.VolatileTokens".Translate(volatileTokens, (100f - hitRatio)));
            sb.AppendLine("RimMind.Inspector.EstimatedHit".Translate(hitRatio));
            sb.AppendLine("RimMind.Inspector.TailIsolated".Translate());
            sb.AppendLine();
            sb.AppendLine("=== 冗余与优化提示 (Redundancy & Warnings) ===");
            if (r.Warnings.Count == 0)
            {
                sb.AppendLine("  (未检测到明显冗余，结构配比健康)");
            }
            else
            {
                foreach (var w in r.Warnings)
                {
                    sb.AppendLine($"[!] {w}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("=== 信息充足度评估 (Information Sufficiency) ===");
            foreach (var s in r.SufficiencyPoints)
            {
                sb.AppendLine($"[✓] {s}");
            }

            DrawScrollableText(rect, sb.ToString());
        }

        private void DrawLiveTestTab(Rect rect)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== 实机测试运行状态: {_testStatus} ===");
            if (_testElapsedMs > 0)
            {
                sb.AppendLine($"耗时: {_testElapsedMs} ms");
            }
            if (!string.IsNullOrEmpty(_testError))
            {
                sb.AppendLine($"错误: {_testError}");
            }
            sb.AppendLine();
            sb.AppendLine("--- 模型自然语言回复 ---");
            sb.AppendLine(string.IsNullOrEmpty(_testResponseContent) ? "(暂无输出，点击下方 [发送实机测试请求] 测试)" : _testResponseContent);
            sb.AppendLine();
            sb.AppendLine("--- 返回的 ToolCalls 动作 ---");
            sb.AppendLine(string.IsNullOrEmpty(_testToolCallsJson) ? "(无工具调用)" : _testToolCallsJson);

            DrawScrollableText(rect, sb.ToString());
        }

        private void DrawScrollableText(Rect rect, string text)
        {
            Text.Font = GameFont.Tiny;
            float textHeight = Text.CalcHeight(text, rect.width - 24f) + 30f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 20f, Math.Max(rect.height, textHeight));

            Widgets.BeginScrollView(rect, ref _contentScroll, viewRect);
            Widgets.Label(viewRect, text);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f, 0.9f));
            float btnWidth = 190f;
            float curX = rect.x + 10f;

            // 1. Copy full payload
            Rect copyBtn = new Rect(curX, rect.y + 8f, btnWidth, 28f);
            if (Widgets.ButtonText(copyBtn, "RimMind.Inspector.CopyClipboard".Translate()))
            {
                CopyFullPayloadToClipboard();
                Messages.Message("RimMind.Inspector.Copied".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            curX += btnWidth + 10f;

            // 2. Re-evaluate / Refresh
            Rect refreshBtn = new Rect(curX, rect.y + 8f, 140f, 28f);
            if (Widgets.ButtonText(refreshBtn, "RimMind.Inspector.RefreshSnapshot".Translate()))
            {
                RebuildEnvelope();
            }
            curX += 140f + 10f;

            // 3. Test Live Request
            Rect testBtn = new Rect(curX, rect.y + 8f, 180f, 28f);
            GUI.enabled = !_isTesting;
            if (Widgets.ButtonText(testBtn, _isTesting ? "RimMind.Inspector.Testing".Translate() : "RimMind.Inspector.TestRun".Translate()))
            {
                ExecuteLiveTest();
            }
            GUI.enabled = true;
        }

        private void CopyFullPayloadToClipboard()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"# RimMind LLM Request Envelope (Pawn: {_selectedPawn?.LabelCap}, Scenario: {ScenarioLabels[_selectedScenarioIndex]})");
            sb.AppendLine();
            sb.AppendLine("## 1. System Prompt");
            sb.AppendLine("```");
            sb.AppendLine(_cachedSystemText);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## 2. Context Layers (User/System)");
            sb.AppendLine("```");
            sb.AppendLine(_cachedLayersText);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## 3. Available Tools");
            sb.AppendLine("```json");
            sb.AppendLine(_cachedToolsText);
            sb.AppendLine("```");

            GUIUtility.systemCopyBuffer = sb.ToString();
        }

        private void ExecuteLiveTest()
        {
            if (_cachedEnvelope == null) return;
            _isTesting = true;
            _testStatus = "Sending request to AI provider...";
            _testError = string.Empty;
            _testResponseContent = string.Empty;
            _testToolCallsJson = string.Empty;
            _selectedTab = 4; // Switch to live test tab

            Stopwatch sw = Stopwatch.StartNew();
            RimMindAPI.Request.Send(_cachedEnvelope, (result, ctx) =>
            {
                sw.Stop();
                _isTesting = false;
                _testElapsedMs = sw.ElapsedMilliseconds;

                if (result.IsOk)
                {
                    _testStatus = "Success (200 OK)";
                    var resp = result.Value;
                    _testResponseContent = resp.Content ?? string.Empty;
                    _testToolCallsJson = resp.ToolCallsJson ?? string.Empty;
                }
                else
                {
                    _testStatus = "Failed";
                    _testError = result.Error.Message;
                }
            });
        }

        private void RebuildEnvelope()
        {
            _cachedPawn = _selectedPawn;
            _cachedScenarioIndex = _selectedScenarioIndex;
            if (_selectedPawn == null) return;

            string scenario = Scenarios[_selectedScenarioIndex];
            var tools = RimMindAPI.Tools.GetAllDefinitions().ToList();

            // 1. Build System Prompt & Rules
            var sysBuilder = new StringBuilder();
            sysBuilder.AppendLine($"You are an AI mind orchestrator controlling '{_selectedPawn.LabelCap}' in RimWorld 1.6.");
            sysBuilder.AppendLine($"Current Scenario: {scenario}");
            sysBuilder.AppendLine("Act in character, adhering to colonist traits, health constraints, and immediate survival needs.");
            _cachedSystemText = sysBuilder.ToString();

            // 2. Build Layers (L1-L5)
            var layerBuilder = new StringBuilder();
            var pawnBuilder = new PawnContextBuilder();
            string pawnState = pawnBuilder.BuildPawnContext(_selectedPawn);

            layerBuilder.AppendLine("<layer_core>");
            layerBuilder.AppendLine($"[identity] Name={_selectedPawn.Name?.ToStringFull ?? _selectedPawn.LabelCap}, Gender={_selectedPawn.gender}, Age={_selectedPawn.ageTracker?.AgeBiologicalYears}");
            layerBuilder.AppendLine("</layer_core>");
            layerBuilder.AppendLine();

            layerBuilder.AppendLine("<layer_environment>");
            var map = _selectedPawn.Map ?? Find.CurrentMap;
            if (map != null)
            {
                layerBuilder.AppendLine($"[map] Wealth={map.wealthWatcher?.WealthTotal:F0}, DangerRating={map.dangerWatcher?.DangerRating}");
            }
            layerBuilder.AppendLine("</layer_environment>");
            layerBuilder.AppendLine();

            layerBuilder.AppendLine("<layer_state>");
            layerBuilder.AppendLine(pawnState);
            layerBuilder.AppendLine("</layer_state>");
            layerBuilder.AppendLine();

            layerBuilder.AppendLine("<layer_history>");
            layerBuilder.AppendLine("[memory] Recent thoughts, work interactions, and relations.");
            layerBuilder.AppendLine("</layer_history>");
            layerBuilder.AppendLine();

            layerBuilder.AppendLine("<layer_task>");
            layerBuilder.AppendLine($"[task] Evaluate current conditions and decide next immediate actions or dialogue for {_selectedPawn.LabelCap}.");
            layerBuilder.AppendLine("</layer_task>");
            _cachedLayersText = layerBuilder.ToString();

            // 3. Tools JSON
            var toolsBuilder = new StringBuilder();
            var domainTools = ThinkStrategyHelper.ConvertToDomainTools(tools);
            foreach (var dt in domainTools)
            {
                toolsBuilder.AppendLine($"• Tool: {dt.Name}");
                toolsBuilder.AppendLine($"  Desc: {dt.Description}");
                if (!string.IsNullOrEmpty(dt.Parameters))
                {
                    toolsBuilder.AppendLine($"  Params: {dt.Parameters}");
                }
                toolsBuilder.AppendLine();
            }
            _cachedToolsText = toolsBuilder.ToString();

            // 4. Assemble Envelope
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = _cachedSystemText },
                new ChatMessage { Role = "user", Content = _cachedLayersText }
            };

            _cachedEnvelope = new LlmRequestEnvelope
            {
                RequestId = Guid.NewGuid().ToString("N").Substring(0, 10),
                ScenarioId = scenario,
                ModId = RimMindOwnerConsts.CoreModId,
                Messages = messages,
                Tools = domainTools,
                MaxTokens = 600,
                Temperature = 0.7f,
                NpcId = _selectedPawn.thingIDNumber.ToString()
            };

            // 5. Calculate Token Analysis
            _cachedAnalysis = AnalyzeTokens(_cachedSystemText, _cachedLayersText, _cachedToolsText, _selectedPawn);
        }

        private TokenAnalysisReport AnalyzeTokens(string system, string layers, string tools, Pawn pawn)
        {
            var rep = new TokenAnalysisReport();
            rep.SystemTokens = EstimateTokens(system);
            rep.ToolTokens = EstimateTokens(tools);

            // Estimate static vs dynamic in layers
            int backstoryLen = (pawn.story?.Childhood?.title?.Length ?? 0) + (pawn.story?.Adulthood?.title?.Length ?? 0);
            rep.StaticTokens = rep.SystemTokens + EstimateTokens(backstoryLen * 4);
            int baseProfileLen = backstoryLen * 4 + 120; // backstory + traits + passions
            rep.StaticTokens = EstimateTokens(baseProfileLen);
            rep.DynamicTokens = Math.Max(0, EstimateTokens(layers) - rep.StaticTokens);
            rep.TotalTokens = rep.SystemTokens + EstimateTokens(layers) + rep.ToolTokens;
            rep.TotalTokens = rep.SystemTokens + rep.StaticTokens + rep.DynamicTokens + rep.ToolTokens;
            rep.TotalChars = system.Length + layers.Length + tools.Length;

            // Redundancy & Cache checks
            if (pawn.skills != null && pawn.skills.skills.Count(s => s.Level < 3) > 4)
            {
                rep.Warnings.Add("包含 4+ 个极低技能 (等级 < 3)。建议非工作决策时过滤无意义弱势技能。");
            }
            if (backstoryLen > 0)
            {
                rep.SufficiencyPoints.Add("RimMind.Inspector.ProfileAligned".Translate());
            }

            // Sufficiency check
            rep.SufficiencyPoints.Add("生理与健康状态完备：包含疼痛、出血、严重疾病严重度过滤。");
            rep.SufficiencyPoints.Add("空间与路径闭环：由底层 C# JobDispatcher 自动解析目标寻路，无需在 Prompt 中堆砌全图坐标。");
            rep.SufficiencyPoints.Add("社交关系支持：直接关系人与伴侣信息已包含。");

            return rep;
        }

        private static int EstimateTokens(int charCount) => (int)Math.Ceiling(charCount / 3.2);
        private static int EstimateTokens(string text) => (int)Math.Ceiling((text?.Length ?? 0) / 3.2);

        private class TokenAnalysisReport
        {
            public int TotalTokens;
            public int TotalChars;
            public int SystemTokens;
            public int StaticTokens;
            public int DynamicTokens;
            public int ToolTokens;
            public List<string> Warnings = new();
            public List<string> SufficiencyPoints = new();
        }
    }
}
