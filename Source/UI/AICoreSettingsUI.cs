using System;
using System.Collections.Generic;
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

        private static string _curTab = "api";

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

        // ── 入口 ─────────────────────────────────────────────────────────────

        public static void Draw(Rect inRect)
        {
            // ── Tab 按钮行（ButtonText 式，不会压住 mod 标题）
            DrawTabBar(new Rect(inRect.x, inRect.y, inRect.width, TabBarHeight));

            // ── 内容区
            Rect content = new Rect(inRect.x, inRect.y + TabBarHeight + TabBarGap,
                                    inRect.width, inRect.height - TabBarHeight - TabBarGap);

            switch (_curTab)
            {
                case "api":     DrawApiTab(content);     break;
                case "context": DrawContextTab(content); break;
                case "prompts": DrawPromptsTab(content); break;
                default:
                    foreach (var (id, _, fn) in RimMindAPI.SettingsTabs)
                        if (id == _curTab) { fn(content); break; }
                    break;
            }
        }

        private static void DrawTabBar(Rect r)
        {
            // 收集所有 tab：内置 2 个 + 子 mod 注册的
            var tabs = new List<(string id, string label)>
            {
                ("api",     "RimMind.Core.Settings.Tab.Api".Translate()),
                ("prompts", "RimMind.Core.Settings.Tab.Prompts".Translate()),
                ("context", "RimMind.Core.Settings.Tab.Context".Translate()),
            };
            foreach (var (id, labelFn, _) in RimMindAPI.SettingsTabs)
                tabs.Add((id, labelFn()));

            float w = r.width / tabs.Count;
            for (int i = 0; i < tabs.Count; i++)
            {
                var (id, label) = tabs[i];
                Rect btn = new Rect(r.x + w * i, r.y, w, TabBarHeight);
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

            var queue = AIRequestQueue.Instance;
            if (queue != null)
            {
                foreach (var kvp in RimMindAPI.ModCooldownGetters)
                {
                    string modId = kvp.Key;
                    int cooldownLeft = queue.GetCooldownTicksLeft(modId);
                    int queueDepth = queue.GetQueueDepth(modId);
                    string status = cooldownLeft > 0
                        ? $"  ({cooldownLeft} ticks)"
                        : "  (ready)";
                    string queueInfo = queueDepth > 0
                        ? $"  [queue: {queueDepth}]"
                        : "";
                    GUI.color = cooldownLeft > 0 ? Color.gray : new Color(0.4f, 0.9f, 0.4f);
                    listing.Label($"{modId}{status}{queueInfo}");
                }
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
            left.CheckboxLabeled("RimMind.Core.Context.IncludeRace".Translate(),           ref ctx.IncludeRace);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeAge".Translate(),            ref ctx.IncludeAge);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeGender".Translate(),         ref ctx.IncludeGender);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeBackstory".Translate(),      ref ctx.IncludeBackstory);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeIdeology".Translate(),       ref ctx.IncludeIdeology);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeTraits".Translate(),         ref ctx.IncludeTraits);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeSkills".Translate(),         ref ctx.IncludeSkills);
            if (ctx.IncludeSkills)
            {
                left.Label($"  {"RimMind.Core.Context.MinSkillLevel".Translate()}: {ctx.MinSkillLevel}");
                ctx.MinSkillLevel = (int)left.Slider(ctx.MinSkillLevel, 1f, 15f);
            }
            left.CheckboxLabeled("RimMind.Core.Context.IncludeHealth".Translate(),         ref ctx.IncludeHealth);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeMood".Translate(),           ref ctx.IncludeMood);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeMoodThoughts".Translate(),   ref ctx.IncludeMoodThoughts);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeCurrentJob".Translate(),     ref ctx.IncludeCurrentJob);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeWorkPriorities".Translate(), ref ctx.IncludeWorkPriorities);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeEquipment".Translate(),      ref ctx.IncludeEquipment);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeLocation".Translate(),       ref ctx.IncludeLocation);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeRelations".Translate(),      ref ctx.IncludeRelations);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeGenes".Translate(),          ref ctx.IncludeGenes);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeCombatStatus".Translate(),   ref ctx.IncludeCombatStatus);
            left.CheckboxLabeled("RimMind.Core.Context.IncludeSurroundings".Translate(),   ref ctx.IncludeSurroundings);
            float leftH = left.CurHeight;
            left.End();

            var right = new Listing_Standard();
            right.Begin(new Rect(anchor.x + colW + 20f, anchor.y, colW, 9999f));
            GUI.color = new Color(0.6f, 0.78f, 1f);
            right.Label("RimMind.Core.Context.Environment".Translate());
            GUI.color = Color.white;
            right.Gap(4f);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeGameTime".Translate(),        ref ctx.IncludeGameTime);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeColonistCount".Translate(), ref ctx.IncludeColonistCount);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeWealth".Translate(),        ref ctx.IncludeWealth);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeFood".Translate(),          ref ctx.IncludeFood);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeSeason".Translate(),        ref ctx.IncludeSeason);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeWeather".Translate(),       ref ctx.IncludeWeather);
            right.CheckboxLabeled("RimMind.Core.Context.IncludeThreats".Translate(),       ref ctx.IncludeThreats);
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
            int modCount = RimMindAPI.ModCooldownGetters.Count;
            h += modCount * 24f;
            h += 24f;
            h += 24f + 24f;
            return h + 40f;
        }

    }
}
