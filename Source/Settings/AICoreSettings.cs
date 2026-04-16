using Verse;

namespace RimMind.Core.Settings
{
    public class RimMindCoreSettings : ModSettings
    {
        // ── API 配置 ──────────────────────────────────────────
        public string apiKey = string.Empty;
        public string apiEndpoint = "https://api.openai.com/v1";
        public string modelName = "gpt-4o-mini";

        // ── 模型行为 ──────────────────────────────────────────
        /// <summary>
        /// 向请求追加 response_format={"type":"json_object"}。
        /// 不支持该参数的本地模型请关闭。
        /// </summary>
        public bool forceJsonMode = true;
        public bool useStreaming = false;

        // ── 性能 ──────────────────────────────────────────────
        public int maxTokens = 800;

        public bool debugLogging = false;

        // ── 上下文过滤 ─────────────────────────────────────────
        public ContextSettings Context = new ContextSettings();

        // ── 全局自定义提示词 ────────────────────────────────────
        public string customPawnPrompt = string.Empty;
        public string customMapPrompt = string.Empty;

        // ── 悬浮窗 ──────────────────────────────────────────────
        public bool requestOverlayEnabled = true;
        public float requestOverlayX = 20f;
        public float requestOverlayY = 20f;
        public float requestOverlayW = 300f;
        public float requestOverlayH = 200f;

        public bool IsConfigured() =>
            !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiEndpoint);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref apiKey,               "apiKey",               string.Empty);
            Scribe_Values.Look(ref apiEndpoint,          "apiEndpoint",          "https://api.deepseek.com/v1");
            Scribe_Values.Look(ref modelName,            "modelName",            "deepseek-chat");
            Scribe_Values.Look(ref forceJsonMode,        "forceJsonMode",        true);
            Scribe_Values.Look(ref useStreaming,         "useStreaming",          false);
            Scribe_Values.Look(ref maxTokens,            "maxTokens",            800);
            Scribe_Values.Look(ref debugLogging,         "debugLogging",         false);
            Scribe_Deep.Look(ref Context,                "Context");
            Context ??= new ContextSettings();
            Scribe_Values.Look(ref customPawnPrompt,     "customPawnPrompt",     string.Empty);
            Scribe_Values.Look(ref customMapPrompt,      "customMapPrompt",      string.Empty);
            Scribe_Values.Look(ref requestOverlayEnabled, "requestOverlayEnabled", true);
            Scribe_Values.Look(ref requestOverlayX,      "requestOverlayX",      20f);
            Scribe_Values.Look(ref requestOverlayY,      "requestOverlayY",      20f);
            Scribe_Values.Look(ref requestOverlayW,      "requestOverlayW",      300f);
            Scribe_Values.Look(ref requestOverlayH,      "requestOverlayH",      200f);
        }
    }
}
