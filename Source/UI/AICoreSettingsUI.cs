using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimMind.Core.Client;
using RimMind.Core.Internal;
using RimMind.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Core.UI
{
    /// <summary>
    /// 多分页设置界面。
    /// 使用 ButtonText 式导航（不占用 mod 标题区域）。
    /// 子 mod 通过 RimMindAPI.RegisterSettingsTab 注册额外分页。
    /// </summary>
    public static class RimMindCoreSettingsUI
    {
        private const float TabBarHeight  = 32f;
        private const float TabBarGap     = 6f;
        private const float TabMinWidth   = 120f;
        private const float TabGap        = 4f;

        private static string _curTab = "api";
        private static float _cachedTabBarHeight = TabBarHeight;

        // API tab state
        private static bool   _showApiKey;
        private static string _testStatus      = "";
        private static Color  _testStatusColor = Color.white;
        private static Vector2 _apiScroll;

        // Context tab state
        private static ContextPreset _selectedPreset = ContextPreset.Custom;
        private static Vector2       _contextScroll;

        // Prompts tab state
        private static Vector2 _promptsScroll;

        // Queue tab state
        private static Vector2 _queueScroll;

        // ── 入口 ─────────────────────────────────────────────────────────────

        public static void Draw(Rect inRect)
        {
            var tabs = CollectTabs();
            _cachedTabBarHeight = CalcTabBarHeight(inRect.width, tabs.Count);

            DrawTabBar(new Rect(inRect.x, inRect.y, inRect.width, _cachedTabBarHeight), tabs);

            Rect content = new Rect(inRect.x, inRect.y + _cachedTabBarHeight + TabBarGap,
                                    inRect.width, inRect.height - _cachedTabBarHeight - TabBarGap);

            switch (_curTab)
            {
                case "api":     DrawApiTab(content);     break;
                case "queue":   DrawQueueTab(content);   break;
                case "context": DrawContextTab(content); break;
                case "prompts": DrawPromptsTab(content); break;
                default:
                    foreach (var (id, _, fn) in RimMindAPI.SettingsTabs)
                        if (id == _curTab) { fn(content); break; }
                    break;
            }
        }

        private static List<(string id, string label)> CollectTabs()
        {
            var tabs = new List<(string id, string label)>
            {
                ("api",     "RimMind.Core.Settings.Tab.Api".Translate()),
                ("queue",   "RimMind.Core.Settings.Tab.Queue".Translate()),
                ("prompts", "RimMind.Core.Settings.Tab.Prompts".Translate()),
                ("context", "RimMind.Core.Settings.Tab.Context".Translate()),
            };
            foreach (var (id, labelFn, _) in RimMindAPI.SettingsTabs)
                tabs.Add((id, labelFn()));
            return tabs;
        }

        private static int CalcMaxPerRow(float availableWidth, int tabCount)
        {
            if (tabCount <= 0) return 1;
            int perRow = Mathf.FloorToInt((availableWidth + TabGap) / (TabMinWidth + TabGap));
            return Mathf.Clamp(perRow, 1, tabCount);
        }

        private static float CalcTabBarHeight(float availableWidth, int tabCount)
        {
            if (tabCount <= 0) return TabBarHeight;
            int perRow = CalcMaxPerRow(availableWidth, tabCount);
            int rows = Mathf.CeilToInt((float)tabCount / perRow);
            return rows * TabBarHeight + (rows - 1) * TabGap;
        }

        private static void DrawTabBar(Rect r, List<(string id, string label)> tabs)
        {
            int count = tabs.Count;
            if (count == 0) return;

            int perRow = CalcMaxPerRow(r.width, count);
            int rows = Mathf.CeilToInt((float)count / perRow);

            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int colsInRow = (row == rows - 1) ? (count - row * perRow) : perRow;

                float w = (r.width - TabGap * (colsInRow - 1)) / colsInRow;
                float x = r.x + col * (w + TabGap);
                float y = r.y + row * (TabBarHeight + TabGap);

                var (id, label) = tabs[i];
                Rect btn = new Rect(x, y, w, TabBarHeight);
                bool selected = _curTab == id;

                GUI.color = selected ? Color.white : Color.gray;
                if (Widgets.ButtonText(btn, label))
                    _curTab = id;
            }
            GUI.color = Color.white;
        }

        // ── API 配置分页 ─────────────────────────────────────────────────────

        private static void DrawApiTab(Rect inRect)
        {
            var s = RimMindCoreMod.Settings;

            float contentH = EstimateApiHeight();
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentH);
            Widgets.BeginScrollView(inRect, ref _apiScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── API 配置 ──────────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Tab.Api".Translate());

            listing.Label("RimMind.Core.Settings.ApiKey".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.ApiKey.Desc".Translate());
            GUI.color = Color.white;
            {
                Rect row    = listing.GetRect(26f);
                float btnW  = 52f;
                Rect field  = new Rect(row.x, row.y, row.width - btnW - 4f, row.height);
                Rect toggle = new Rect(field.xMax + 4f, row.y, btnW, row.height);

                if (_showApiKey)
                    s.apiKey = Widgets.TextField(field, s.apiKey);
                else
                {
                    GUI.enabled = false;
                    Widgets.TextField(field, new string('•', s.apiKey?.Length ?? 0));
                    GUI.enabled = true;
                }
                if (Widgets.ButtonText(toggle, _showApiKey ? "RimMind.Core.Settings.Hide".Translate() : "RimMind.Core.Settings.Show".Translate()))
                    _showApiKey = !_showApiKey;
            }

            listing.Gap(4f);
            listing.Label("RimMind.Core.Settings.ApiEndpoint".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.ApiEndpoint.Desc".Translate());
            GUI.color = Color.white;
            s.apiEndpoint = listing.TextEntry(s.apiEndpoint);

            listing.Gap(4f);
            listing.Label("RimMind.Core.Settings.ModelName".Translate());
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.ModelName.Desc".Translate());
            GUI.color = Color.white;
            s.modelName = listing.TextEntry(s.modelName);

            listing.Gap(10f);

            // ── 测试连接 ──────────────────────────────────────────────────────
            {
                Rect row    = listing.GetRect(28f);
                Rect btn    = new Rect(row.x, row.y, 110f, row.height);
                Rect status = new Rect(btn.xMax + 8f, row.y + 4f, row.width - 120f, row.height);
                if (Widgets.ButtonText(btn, "RimMind.Core.Settings.TestConnection".Translate()))
                    RunConnectionTest(s);
                GUI.color = _testStatusColor;
                Widgets.Label(status, _testStatus);
                GUI.color = Color.white;
            }

            listing.Gap(6f);

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Section.ModelBehavior".Translate());
            listing.CheckboxLabeled(
                "RimMind.Core.Settings.ForceJsonMode".Translate(),
                ref s.forceJsonMode,
                "RimMind.Core.Settings.ForceJsonModeDesc".Translate());

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Section.Request".Translate());
            listing.Label($"{"RimMind.Core.Settings.MaxTokens".Translate()}: {s.maxTokens}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.MaxTokens.Desc".Translate());
            GUI.color = Color.white;
            s.maxTokens = (int)listing.Slider(s.maxTokens, 200f, 2000f);

            listing.Label($"{"RimMind.Core.Settings.MaxConcurrent".Translate()}: {s.maxConcurrentRequests}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.MaxConcurrent.Desc".Translate());
            GUI.color = Color.white;
            s.maxConcurrentRequests = (int)listing.Slider(s.maxConcurrentRequests, 1f, 10f);

            listing.Label($"{"RimMind.Core.Settings.MaxRetry".Translate()}: {s.maxRetryCount}");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.MaxRetry.Desc".Translate());
            GUI.color = Color.white;
            s.maxRetryCount = (int)listing.Slider(s.maxRetryCount, 0f, 5f);

            listing.Label($"{"RimMind.Core.Settings.RequestTimeout".Translate()}: {s.requestTimeoutMs / 1000}s");
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Core.Settings.RequestTimeout.Desc".Translate());
            GUI.color = Color.white;
            s.requestTimeoutMs = (int)listing.Slider(s.requestTimeoutMs / 1000f, 10f, 300f) * 1000;

            var queue = AIRequestQueue.Instance;
            if (queue != null)
            {
                listing.Gap(4f);
                GUI.color = Color.gray;
                listing.Label("RimMind.Core.Settings.QueueSeeTab".Translate());
                GUI.color = Color.white;
            }
            GUI.color = Color.white;

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Section.Debug".Translate());
            listing.CheckboxLabeled("RimMind.Core.Settings.DebugLogging".Translate(), ref s.debugLogging,
                "RimMind.Core.Settings.DebugLogging.Desc".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 使用 UnityWebRequest 发请求（与实际 AI 请求一致，确保测试结果真实）。
        /// </summary>
        private static void RunConnectionTest(RimMindCoreSettings s)
        {
            if (!s.IsConfigured())
            {
                _testStatus      = "RimMind.Core.Settings.Status.NotConfigured".Translate();
                _testStatusColor = Color.yellow;
                return;
            }

            _testStatus      = "RimMind.Core.Settings.Status.Testing".Translate();
            _testStatusColor = Color.yellow;

            string endpoint = FormatEndpoint(s.apiEndpoint);
            string apiKey   = s.apiKey;
            string model    = s.modelName;
            Log.Message($"[RimMind] Test connection → {endpoint}  model={model}");

            Task.Run(async () =>
            {
                try
                {
                    var body = new
                    {
                        model    = model,
                        messages = new[]
                        {
                            new { role = "user", content = "RimMind.Core.Settings.TestMessage".Translate() }
                        },
                        max_tokens  = 60,
                        temperature = 0.7f,
                        stream      = false,
                    };
                    string json = JsonConvert.SerializeObject(body);

                    string text = await PostAsync(endpoint, json, apiKey);

                    var    jobj   = JObject.Parse(text);
                    string reply  = jobj["choices"]?[0]?["message"]?["content"]?.ToString() ?? "RimMind.Core.UI.Empty".Translate();
                    int    tokens = jobj["usage"]?["total_tokens"]?.Value<int>() ?? 0;

                    _testStatus      = $"✓ {reply.Trim()} ({tokens} tok)";
                    _testStatusColor = new Color(0.4f, 0.9f, 0.4f);
                }
                catch (Exception ex)
                {
                    AIRequestQueue.LogFromBackground($"[RimMind] Test exception: {ex.Message}", isWarning: true);
                    _testStatus      = $"✗ {ex.Message}";
                    _testStatusColor = new Color(0.9f, 0.4f, 0.4f);
                }
            });
        }

        private static async Task<string> PostAsync(string url, string jsonBody, string apiKey)
        {
            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(
                System.Text.Encoding.UTF8.GetBytes(jsonBody));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            var asyncOp = webRequest.SendWebRequest();

            float timeout = 30f;
            float elapsed = 0f;

            while (!asyncOp.isDone)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
                if (elapsed > timeout)
                {
                    webRequest.Abort();
                    throw new TimeoutException($"Timeout after {timeout}s");
                }
            }

            if (webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ||
                webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
            {
                string body = webRequest.downloadHandler?.text ?? "";
                string err  = body.Length > 0 ? body : webRequest.error;
                throw new Exception($"HTTP {webRequest.responseCode}: {err}");
            }

            return webRequest.downloadHandler.text;
        }

        private static string FormatEndpoint(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
            string trimmed = baseUrl.Trim().TrimEnd('/');
            // Already a full endpoint URL
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            var uri = new Uri(trimmed);
            string path = uri.AbsolutePath.Trim('/');
            // Has versioned base path (e.g. /v1) → append /chat/completions only
            if (!string.IsNullOrEmpty(path))
                return trimmed + "/chat/completions";
            // Bare domain → append full path
            return trimmed + "/v1/chat/completions";
        }

        // ── 队列状态分页 ──────────────────────────────────────────────────────

        private static void DrawQueueTab(Rect inRect)
        {
            var queue = AIRequestQueue.Instance;
            if (queue == null)
            {
                var listing0 = new Listing_Standard();
                listing0.Begin(inRect);
                GUI.color = Color.yellow;
                listing0.Label("RimMind.Core.Settings.QueueNotAvailable".Translate());
                GUI.color = Color.white;
                listing0.End();
                return;
            }

            int modCount = queue.GetAllQueueDepths().Count;
            int activeCount = queue.ActiveRequestCount;
            int queuedCount = queue.TotalQueuedCount;
            float contentH = 60f + 28f + modCount * 26f + 28f + activeCount * 24f + 28f + queuedCount * 24f + 80f;
            contentH = Mathf.Max(contentH, inRect.height + 10f);

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentH);
            Widgets.BeginScrollView(inRect, ref _queueScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── 总体状态 ──────────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Queue.Status".Translate());

            string pauseLabel = queue.IsPaused
                ? "RimMind.Core.Settings.QueuePaused".Translate()
                : "RimMind.Core.Settings.QueueRunning".Translate();
            GUI.color = queue.IsPaused ? Color.yellow : new Color(0.4f, 0.9f, 0.4f);
            listing.Label(pauseLabel);
            GUI.color = Color.white;

            listing.Label($"{"RimMind.Core.Settings.Queue.Active".Translate()}: {activeCount} / {RimMindCoreMod.Settings.maxConcurrentRequests}");
            listing.Label($"{"RimMind.Core.Settings.Queue.Queued".Translate()}: {queuedCount}");
            GUI.color = queue.IsLocalModelBusy ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
            listing.Label($"{"RimMind.Core.Settings.Queue.LocalModel".Translate()}: {(queue.IsLocalModelBusy ? "RimMind.Core.Settings.Queue.Busy".Translate() : "RimMind.Core.Settings.Queue.Idle".Translate())}");
            GUI.color = Color.white;

            // ── 操作按钮 ──────────────────────────────────────────────────────
            listing.Gap(4f);
            Rect btnRow = listing.GetRect(28f);
            float btnW = 110f;
            float gap = 8f;

            Rect pauseBtn = new Rect(btnRow.x, btnRow.y, btnW, btnRow.height);
            Rect clearBtn = new Rect(pauseBtn.xMax + gap, btnRow.y, btnW, btnRow.height);
            Rect clearCdBtn = new Rect(clearBtn.xMax + gap, btnRow.y, btnW + 20f, btnRow.height);

            string pauseText = queue.IsPaused
                ? "RimMind.Core.Settings.Queue.Resume".Translate()
                : "RimMind.Core.Settings.Queue.Pause".Translate();
            if (Widgets.ButtonText(pauseBtn, pauseText))
            {
                if (queue.IsPaused) queue.ResumeQueue();
                else queue.PauseQueue();
            }
            if (Widgets.ButtonText(clearBtn, "RimMind.Core.Settings.Queue.ClearQueues".Translate()))
                queue.ClearAllQueues();
            if (Widgets.ButtonText(clearCdBtn, "RimMind.Core.Settings.Queue.ClearCooldowns".Translate()))
                queue.ClearAllCooldowns();

            // ── 各 Mod 队列 ──────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Queue.PerMod".Translate());

            var allDepths = queue.GetAllQueueDepths();
            var allCooldowns = queue.GetAllCooldowns();
            var allModIds = new HashSet<string>(allDepths.Keys);
            allModIds.UnionWith(allCooldowns.Keys);
            allModIds.UnionWith(RimMindAPI.ModCooldownGetters.Keys);

            if (allModIds.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Core.Settings.Queue.NoMods".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (string modId in allModIds.OrderBy(id => id))
                {
                    int depth = allDepths.TryGetValue(modId, out var d) ? d : 0;
                    int cooldownLeft = queue.GetCooldownTicksLeft(modId);
                    float cooldownSec = cooldownLeft / 60f;

                    string cooldownStr = cooldownLeft > 0
                        ? $"{"RimMind.Core.Settings.Queue.Cooldown".Translate()}: {cooldownSec:F1}s"
                        : "RimMind.Core.Settings.Queue.Ready".Translate();
                    string depthStr = depth > 0
                        ? $"  [{"RimMind.Core.Settings.Queue.QueueCount".Translate()}: {depth}]"
                        : "";

                    GUI.color = cooldownLeft > 0 ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 0.4f);
                    listing.Label($"{modId}  {cooldownStr}{depthStr}");
                }
            }
            GUI.color = Color.white;

            // ── 活跃请求 ──────────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Queue.ActiveRequests".Translate());

            var activeRequests = queue.GetActiveRequests();
            if (activeRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Core.Settings.Queue.NoActive".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (var req in activeRequests)
                {
                    int elapsedTicks = Find.TickManager.TicksGame - req.StartedProcessingAtTick;
                    float elapsedSec = elapsedTicks / 60f;
                    string priority = req.Request.Priority.ToString();
                    string info = $"[{req.Request.ModId}] {req.Request.RequestId}  " +
                                  $"{"RimMind.Core.Settings.Queue.Priority".Translate()}: {priority}  " +
                                  $"{"RimMind.Core.Settings.Queue.Attempt".Translate()}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{"RimMind.Core.Settings.Queue.Elapsed".Translate()}: {elapsedSec:F1}s";
                    GUI.color = new Color(0.7f, 0.85f, 1f);
                    listing.Label(info);
                }
            }
            GUI.color = Color.white;

            // ── 排队请求 ──────────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Settings.Queue.QueuedRequests".Translate());

            var queuedRequests = queue.GetAllQueuedRequests();
            if (queuedRequests.Count == 0)
            {
                GUI.color = Color.gray;
                listing.Label("RimMind.Core.Settings.Queue.NoQueued".Translate());
                GUI.color = Color.white;
            }
            else
            {
                foreach (var req in queuedRequests)
                {
                    int waitTicks = Find.TickManager.TicksGame - req.EnqueuedAtTick;
                    float waitSec = waitTicks / 60f;
                    string priority = req.Request.Priority.ToString();
                    string info = $"[{req.Request.ModId}] {req.Request.RequestId}  " +
                                  $"{"RimMind.Core.Settings.Queue.Priority".Translate()}: {priority}  " +
                                  $"{"RimMind.Core.Settings.Queue.Attempt".Translate()}: {req.AttemptCount}/{req.MaxAttempts}  " +
                                  $"{"RimMind.Core.Settings.Queue.Waiting".Translate()}: {waitSec:F1}s";
                    GUI.color = new Color(0.85f, 0.85f, 0.7f);
                    listing.Label(info);
                }
            }
            GUI.color = Color.white;

            listing.End();
            Widgets.EndScrollView();
        }

        // ── 自定义提示词分页 ──────────────────────────────────────────────────

        private static void DrawPromptsTab(Rect inRect)
        {
            var s = RimMindCoreMod.Settings;

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 460f);
            Widgets.BeginScrollView(inRect, ref _promptsScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            GUI.color = Color.gray;
            listing.Label("RimMind.Core.Prompts.Desc".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);

            SettingsUIHelper.DrawCustomPromptSection(listing,
                "RimMind.Core.Prompts.PawnPromptLabel".Translate(),
                ref s.customPawnPrompt, 100f);

            listing.Gap(12f);

            SettingsUIHelper.DrawCustomPromptSection(listing,
                "RimMind.Core.Prompts.MapPromptLabel".Translate(),
                ref s.customMapPrompt, 100f);

            listing.End();
            Widgets.EndScrollView();
        }

        // ── 上下文过滤分页 ────────────────────────────────────────────────────

        private static void DrawContextTab(Rect inRect)
        {
            var ctx = RimMindCoreMod.Settings.Context;

            // 估算内容高度（用 ScrollView）
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 880f);
            Widgets.BeginScrollView(inRect, ref _contextScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            GUI.color = Color.gray;
            listing.Label("RimMind.Core.Context.Desc".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);

            // ── 预设卡片 ─────────────────────────────────────────────────────
            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Core.Context.Presets".Translate());
            DrawPresetCards(listing, ctx);
            listing.Gap(12f);

            // ── 两栏复选框 ───────────────────────────────────────────────────
            float colW   = (listing.ColumnWidth - 20f) / 2f;
            Rect anchor  = listing.GetRect(0f);

            var left = new Listing_Standard();
            left.Begin(new Rect(anchor.x, anchor.y, colW, 9999f));
            GUI.color = new Color(0.6f, 0.78f, 1f);
            left.Label("RimMind.Core.Context.PawnInfo".Translate());
            GUI.color = Color.white;
            left.Gap(4f);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeRace".Translate(),           ref ctx.IncludeRace,           "RimMind.Core.Context.IncludeRace.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeAge".Translate(),            ref ctx.IncludeAge,            "RimMind.Core.Context.IncludeAge.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeGender".Translate(),         ref ctx.IncludeGender,         "RimMind.Core.Context.IncludeGender.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeBackstory".Translate(),      ref ctx.IncludeBackstory,      "RimMind.Core.Context.IncludeBackstory.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeIdeology".Translate(),       ref ctx.IncludeIdeology,       "RimMind.Core.Context.IncludeIdeology.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeTraits".Translate(),         ref ctx.IncludeTraits,         "RimMind.Core.Context.IncludeTraits.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeSkills".Translate(),         ref ctx.IncludeSkills,         "RimMind.Core.Context.IncludeSkills.Desc".Translate());
            if (ctx.IncludeSkills)
            {
                left.Label($"  {"RimMind.Core.Context.MinSkillLevel".Translate()}: {ctx.MinSkillLevel}");
                ctx.MinSkillLevel = (int)left.Slider(ctx.MinSkillLevel, 1f, 15f);
            }
            left.CheckboxLabeled("RimMind.Core.Context.IncludeHealth".Translate(),         ref ctx.IncludeHealth,         "RimMind.Core.Context.IncludeHealth.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeCapacities".Translate(),     ref ctx.IncludeCapacities,     "RimMind.Core.Context.IncludeCapacities.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeMood".Translate(),           ref ctx.IncludeMood,           "RimMind.Core.Context.IncludeMood.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeMoodThoughts".Translate(),   ref ctx.IncludeMoodThoughts,   "RimMind.Core.Context.IncludeMoodThoughts.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeCurrentJob".Translate(),     ref ctx.IncludeCurrentJob,     "RimMind.Core.Context.IncludeCurrentJob.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeWorkPriorities".Translate(), ref ctx.IncludeWorkPriorities, "RimMind.Core.Context.IncludeWorkPriorities.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeEquipment".Translate(),      ref ctx.IncludeEquipment,      "RimMind.Core.Context.IncludeEquipment.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeInventory".Translate(),      ref ctx.IncludeInventory,      "RimMind.Core.Context.IncludeInventory.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeLocation".Translate(),       ref ctx.IncludeLocation,       "RimMind.Core.Context.IncludeLocation.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeRelations".Translate(),      ref ctx.IncludeRelations,      "RimMind.Core.Context.IncludeRelations.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeGenes".Translate(),          ref ctx.IncludeGenes,          "RimMind.Core.Context.IncludeGenes.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeCombatStatus".Translate(),   ref ctx.IncludeCombatStatus,   "RimMind.Core.Context.IncludeCombatStatus.Desc".Translate());
            left.CheckboxLabeled("RimMind.Core.Context.IncludeSurroundings".Translate(),   ref ctx.IncludeSurroundings,   "RimMind.Core.Context.IncludeSurroundings.Desc".Translate());
            float leftH = left.CurHeight;
            left.End();

            var right = new Listing_Standard();
            right.Begin(new Rect(anchor.x + colW + 20f, anchor.y, colW, 9999f));
            GUI.color = new Color(0.6f, 0.78f, 1f);
            right.Label("RimMind.Core.Context.Environment".Translate());
            GUI.color = Color.white;
            right.Gap(4f);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeGameTime".Translate(),        ref ctx.IncludeGameTime,        "RimMind.Core.Context.IncludeGameTime.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeColonistCount".Translate(), ref ctx.IncludeColonistCount, "RimMind.Core.Context.IncludeColonistCount.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeColonistNames".Translate(), ref ctx.IncludeColonistNames, "RimMind.Core.Context.IncludeColonistNames.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeWealth".Translate(),        ref ctx.IncludeWealth,        "RimMind.Core.Context.IncludeWealth.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeFood".Translate(),          ref ctx.IncludeFood,          "RimMind.Core.Context.IncludeFood.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeSeason".Translate(),        ref ctx.IncludeSeason,        "RimMind.Core.Context.IncludeSeason.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeWeather".Translate(),       ref ctx.IncludeWeather,       "RimMind.Core.Context.IncludeWeather.Desc".Translate());
            right.CheckboxLabeled("RimMind.Core.Context.IncludeThreats".Translate(),       ref ctx.IncludeThreats,       "RimMind.Core.Context.IncludeThreats.Desc".Translate());
            float rightH = right.CurHeight;
            right.End();

            listing.Gap(Mathf.Max(leftH, rightH) + 8f);

            if (listing.ButtonText("RimMind.Core.Context.ResetDefault".Translate()))
            {
                RimMindCoreMod.Settings.Context = new ContextSettings();
                _selectedPreset = ContextPreset.Standard;
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPresetCards(Listing_Standard listing, ContextSettings ctx)
        {
            var presets = new[] { ContextPreset.Minimal, ContextPreset.Standard, ContextPreset.Full, ContextPreset.Custom };
            const float gap = 10f;
            const float h   = 62f;
            float totalW    = listing.ColumnWidth;
            float w         = (totalW - gap * (presets.Length - 1)) / presets.Length;
            Rect row        = listing.GetRect(h);

            for (int i = 0; i < presets.Length; i++)
            {
                var  preset   = presets[i];
                bool selected = _selectedPreset == preset;
                Rect box      = new Rect(row.x + (w + gap) * i, row.y, w, h);

                Widgets.DrawBoxSolid(box,
                    selected ? new Color(0.2f, 0.4f, 0.6f, 0.85f) : new Color(0.18f, 0.18f, 0.18f, 0.55f));
                GUI.color = selected ? new Color(0.4f, 0.7f, 1f) : new Color(0.45f, 0.45f, 0.45f);
                Widgets.DrawBox(box, 2);
                GUI.color = Color.white;

                if (Mouse.IsOver(box)) Widgets.DrawHighlight(box);
                if (Widgets.ButtonInvisible(box))
                {
                    _selectedPreset = preset;
                    if (preset != ContextPreset.Custom)
                        ctx.ApplyPreset(preset);
                }

                Rect inner = box.ContractedBy(6f);
                Text.Anchor = TextAnchor.UpperCenter;

                GUI.color = selected ? Color.white : new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, Text.LineHeight),
                    $"RimMind.Core.Context.Preset.{preset}".Translate());

                Text.Font = GameFont.Tiny;
                GUI.color = selected ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.55f, 0.55f, 0.55f);
                Widgets.Label(new Rect(inner.x, inner.y + Text.LineHeight + 2f,
                                       inner.width, inner.height - Text.LineHeight - 2f),
                    $"RimMind.Core.Context.Preset.{preset}.Desc".Translate());

                Text.Font   = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color   = Color.white;
            }

            listing.Gap(4f);
        }

        // ── 辅助 ─────────────────────────────────────────────────────────────

        private static float EstimateApiHeight()
        {
            float h = 30f;
            h += 24f + 26f + 4f + 24f + 4f + 24f + 10f + 28f;
            h += 24f + 24f;
            h += 24f + 24f + 32f;
            h += 24f + 24f;
            h += 24f + 24f + 32f;
            h += 24f;
            h += 24f;
            h += 24f + 24f;
            return h + 40f;
        }

    }
}
