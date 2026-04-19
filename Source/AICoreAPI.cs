using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Core.Client;
using RimMind.Core.Client.OpenAI;
using RimMind.Core.Internal;
using RimMind.Core.Prompt;
using RimMind.Core.Settings;
using RimMind.Core.UI;
using Verse;

namespace RimMind.Core
{
    public static class RimMindAPI
    {
        // ── Provider 注册表 ───────────────────────────────────────────────────

        private static readonly Dictionary<string, (string modId, Func<string?> provider, int priority)>
            _staticProviders = new Dictionary<string, (string, Func<string?>, int)>();

        private static readonly Dictionary<string, (string modId, Func<string, string> provider, int priority)>
            _dynamicProviders = new Dictionary<string, (string, Func<string, string>, int)>();

        private static readonly Dictionary<string, (string modId, Func<Pawn, string?> provider, int priority)>
            _pawnProviders = new Dictionary<string, (string, Func<Pawn, string?>, int)>();

        private static readonly List<(string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)>
            _settingsTabs = new List<(string, Func<string>, Action<UnityEngine.Rect>)>();

        private static readonly List<(string id, Func<bool> isActive, Action toggle)>
            _toggleBehaviors = new List<(string, Func<bool>, Action)>();

        private static readonly Dictionary<string, Func<int>> _modCooldownGetters
            = new Dictionary<string, Func<int>>();

        private static Action<Pawn, string, Pawn?>? _dialogueTriggerFn;

        private static readonly Dictionary<string, Func<Pawn, string, bool>> _dialogueSkipChecks
            = new Dictionary<string, Func<Pawn, string, bool>>();

        private static readonly Dictionary<string, Func<bool>> _floatMenuSkipChecks
            = new Dictionary<string, Func<bool>>();

        private static readonly Dictionary<string, Func<string, bool>> _actionSkipChecks
            = new Dictionary<string, Func<string, bool>>();

        // ── 核心请求 API ──────────────────────────────────────────────────────

        public static void RequestAsync(AIRequest request, Action<AIResponse> onComplete)
        {
            var queue = AIRequestQueue.Instance;
            if (queue == null)
            {
                Log.Error("[RimMind] AIRequestQueue not initialized.");
                return;
            }
            var client = GetClient();
            if (client == null)
            {
                onComplete?.Invoke(AIResponse.Failure(request.RequestId, "AI client not configured."));
                return;
            }
            queue.Enqueue(request, onComplete, client);
        }

        public static void RequestImmediate(AIRequest request, Action<AIResponse> onComplete)
        {
            var queue = AIRequestQueue.Instance;
            if (queue == null)
            {
                Log.Error("[RimMind] AIRequestQueue not initialized.");
                return;
            }
            var client = GetClient();
            if (client == null)
            {
                onComplete?.Invoke(AIResponse.Failure(request.RequestId, "AI client not configured."));
                return;
            }
            queue.EnqueueImmediate(request, onComplete, client);
        }

        public static bool CancelRequest(string requestId)
            => AIRequestQueue.Instance?.CancelRequest(requestId) ?? false;

        public static void PauseQueue() => AIRequestQueue.Instance?.PauseQueue();

        public static void ResumeQueue() => AIRequestQueue.Instance?.ResumeQueue();

        public static bool IsQueuePaused => AIRequestQueue.Instance?.IsPaused ?? false;

        public static int ActiveRequestCount => AIRequestQueue.Instance?.ActiveRequestCount ?? 0;

        public static IReadOnlyList<AIRequestQueue.TrackedRequest> GetActiveRequests()
            => AIRequestQueue.Instance?.GetActiveRequests() ?? new List<AIRequestQueue.TrackedRequest>();

        public static IReadOnlyList<AIRequestQueue.TrackedRequest> GetAllQueuedRequests()
            => AIRequestQueue.Instance?.GetAllQueuedRequests() ?? new List<AIRequestQueue.TrackedRequest>();

        public static int TotalQueuedCount => AIRequestQueue.Instance?.TotalQueuedCount ?? 0;

        // ── 上下文构建 ────────────────────────────────────────────────────────

        public static string BuildMapContext(Map map, bool brief = false)
            => GameContextBuilder.BuildMapContext(map, brief);

        public static string BuildPawnContext(Pawn pawn)
            => GameContextBuilder.BuildPawnContext(pawn);

        public static string BuildHistoryContext(int maxEntries = 10)
            => GameContextBuilder.BuildHistoryContext(maxEntries);

        public static string BuildStaticContext()
        {
            var sb = new StringBuilder();
            foreach (var kvp in _staticProviders)
            {
                try
                {
                    string? seg = kvp.Value.provider();
                    if (!string.IsNullOrEmpty(seg)) sb.AppendLine(seg);
                }
                catch (Exception ex) { Log.Warning($"[RimMind] StaticProvider '{kvp.Key}' error: {ex.Message}"); }
            }
            return sb.ToString().TrimEnd();
        }

        public static string BuildFullPawnPrompt(
            Pawn pawn,
            string? currentQuery = null,
            string[]? excludeProviders = null)
        {
            var sb = new StringBuilder();

            foreach (var kvp in _staticProviders)
            {
                try
                {
                    string? seg = kvp.Value.provider();
                    if (!string.IsNullOrEmpty(seg)) sb.AppendLine(seg);
                }
                catch (Exception ex) { Log.Warning($"[RimMind] StaticProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            var ctx = RimMindCoreMod.Settings?.Context;
            foreach (var kvp in _pawnProviders)
            {
                if (ctx?.disabledProviders?.Contains(kvp.Key) == true) continue;
                if (excludeProviders != null && Array.IndexOf(excludeProviders, kvp.Key) >= 0) continue;
                try
                {
                    string? seg = kvp.Value.provider(pawn);
                    if (!string.IsNullOrEmpty(seg)) sb.AppendLine(seg);
                }
                catch (Exception ex) { Log.Warning($"[RimMind] PawnProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            sb.AppendLine(GameContextBuilder.BuildPawnContext(pawn));

            if (pawn.Map != null)
                sb.AppendLine(GameContextBuilder.BuildMapContext(pawn.Map, brief: false));

            foreach (var kvp in _dynamicProviders)
            {
                try
                {
                    string? seg = kvp.Value.provider(currentQuery ?? string.Empty);
                    if (!string.IsNullOrEmpty(seg)) sb.AppendLine(seg);
                }
                catch (Exception ex) { Log.Warning($"[RimMind] DynamicProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            var settings = RimMindCoreMod.Settings;
            if (settings == null) return sb.ToString().TrimEnd();
            string? customPawn = settings.customPawnPrompt?.Trim();
            if (!string.IsNullOrEmpty(customPawn))
                sb.AppendLine("\n" + "RimMind.Core.Prompt.CustomPawnHeader".Translate() + "\n" + customPawn);
            string? customMap = settings.customMapPrompt?.Trim();
            if (!string.IsNullOrEmpty(customMap))
                sb.AppendLine("\n" + "RimMind.Core.Prompt.CustomMapHeader".Translate() + "\n" + customMap);

            return sb.ToString().TrimEnd();
        }

        public static string BuildFullPawnPrompt(
            Pawn pawn,
            PromptBudget budget,
            string? currentQuery = null,
            string[]? excludeProviders = null)
        {
            var sections = BuildFullPawnSections(pawn, currentQuery, excludeProviders);
            return budget.ComposeToString(sections);
        }

        public static List<PromptSection> BuildFullPawnSections(
            Pawn pawn,
            string? currentQuery = null,
            string[]? excludeProviders = null)
        {
            var sections = new List<PromptSection>();
            var ctx = RimMindCoreMod.Settings?.Context;

            foreach (var kvp in _staticProviders)
            {
                try
                {
                    string? seg = kvp.Value.provider();
                    if (!string.IsNullOrEmpty(seg))
                        sections.Add(new PromptSection(kvp.Key, seg!, kvp.Value.priority));
                }
                catch (Exception ex) { Log.Warning($"[RimMind] StaticProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            foreach (var kvp in _pawnProviders)
            {
                if (ctx?.disabledProviders?.Contains(kvp.Key) == true) continue;
                if (excludeProviders != null && Array.IndexOf(excludeProviders, kvp.Key) >= 0) continue;
                try
                {
                    string? seg = kvp.Value.provider(pawn);
                    if (!string.IsNullOrEmpty(seg))
                        sections.Add(new PromptSection(kvp.Key, seg!, kvp.Value.priority));
                }
                catch (Exception ex) { Log.Warning($"[RimMind] PawnProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            sections.Add(GameContextBuilder.BuildPawnContextSection(pawn));

            if (pawn.Map != null)
                sections.Add(GameContextBuilder.BuildMapContextSection(pawn.Map, brief: false));

            foreach (var kvp in _dynamicProviders)
            {
                try
                {
                    string? seg = kvp.Value.provider(currentQuery ?? string.Empty);
                    if (!string.IsNullOrEmpty(seg))
                        sections.Add(new PromptSection(kvp.Key, seg!, kvp.Value.priority));
                }
                catch (Exception ex) { Log.Warning($"[RimMind] DynamicProvider '{kvp.Key}' error: {ex.Message}"); }
            }

            var settings = RimMindCoreMod.Settings;
            if (settings != null)
            {
                string? customPawn = settings.customPawnPrompt?.Trim();
                if (!string.IsNullOrEmpty(customPawn))
                    sections.Add(new PromptSection("custom_pawn",
                        "RimMind.Core.Prompt.CustomPawnHeader".Translate() + "\n" + customPawn,
                        PromptSection.PriorityCustom));
                string? customMap = settings.customMapPrompt?.Trim();
                if (!string.IsNullOrEmpty(customMap))
                    sections.Add(new PromptSection("custom_map",
                        "RimMind.Core.Prompt.CustomMapHeader".Translate() + "\n" + customMap,
                        PromptSection.PriorityCustom));
            }

            return sections;
        }

        // ── 状态查询 ──────────────────────────────────────────────────────────

        public static bool IsConfigured() => RimMindCoreMod.Settings.IsConfigured();

        // ── Provider 注册（去重/覆盖） ──────────────────────────────────────────

        public static void RegisterStaticProvider(string category, Func<string?> provider,
            int priority = PromptSection.PriorityAuxiliary, string modId = "", bool overrideExisting = true)
        {
            if (_staticProviders.ContainsKey(category))
            {
                if (!overrideExisting) return;
                _staticProviders[category] = (modId, provider, priority);
            }
            else
            {
                _staticProviders[category] = (modId, provider, priority);
            }
        }

        public static void RegisterDynamicProvider(string category, Func<string, string> provider,
            int priority = PromptSection.PriorityAuxiliary, string modId = "", bool overrideExisting = true)
        {
            if (_dynamicProviders.ContainsKey(category))
            {
                if (!overrideExisting) return;
                _dynamicProviders[category] = (modId, provider, priority);
            }
            else
            {
                _dynamicProviders[category] = (modId, provider, priority);
            }
        }

        public static void RegisterPawnContextProvider(string category, Func<Pawn, string?> provider,
            int priority = PromptSection.PriorityAuxiliary, string modId = "", bool overrideExisting = true)
        {
            if (_pawnProviders.ContainsKey(category))
            {
                if (!overrideExisting) return;
                _pawnProviders[category] = (modId, provider, priority);
            }
            else
            {
                _pawnProviders[category] = (modId, provider, priority);
            }
        }

        // ── Provider 查询（供外部 Mod 读取 RimMind 数据） ──────────────────────

        public static string? GetProviderData(string category, Pawn pawn)
        {
            if (!_pawnProviders.TryGetValue(category, out var entry)) return null;
            var ctx = RimMindCoreMod.Settings?.Context;
            if (ctx?.exposedProviders.Count > 0 && !ctx.exposedProviders.Contains(category)) return null;
            try { return entry.provider(pawn); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static string? GetStaticProviderData(string category)
        {
            if (!_staticProviders.TryGetValue(category, out var entry)) return null;
            var ctx = RimMindCoreMod.Settings?.Context;
            if (ctx?.exposedProviders.Count > 0 && !ctx.exposedProviders.Contains(category)) return null;
            try { return entry.provider(); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetStaticProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static string? GetDynamicProviderData(string category, string query)
        {
            if (!_dynamicProviders.TryGetValue(category, out var entry)) return null;
            var ctx = RimMindCoreMod.Settings?.Context;
            if (ctx?.exposedProviders.Count > 0 && !ctx.exposedProviders.Contains(category)) return null;
            try { return entry.provider(query); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetDynamicProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static List<string> GetRegisteredCategories()
        {
            var all = new HashSet<string>();
            all.UnionWith(_staticProviders.Keys);
            all.UnionWith(_pawnProviders.Keys);
            all.UnionWith(_dynamicProviders.Keys);

            var ctx = RimMindCoreMod.Settings?.Context;
            if (ctx?.exposedProviders.Count > 0)
                all.IntersectWith(ctx.exposedProviders);

            return all.ToList();
        }

        // ── Provider 卸载 ──────────────────────────────────────────────────────

        public static void UnregisterPawnContextProvider(string category)
            => _pawnProviders.Remove(category);

        public static void UnregisterStaticProvider(string category)
            => _staticProviders.Remove(category);

        public static void UnregisterDynamicProvider(string category)
            => _dynamicProviders.Remove(category);

        public static void UnregisterModProviders(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            var staticKeys = _staticProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in staticKeys) _staticProviders.Remove(key);

            var dynamicKeys = _dynamicProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in dynamicKeys) _dynamicProviders.Remove(key);

            var pawnKeys = _pawnProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in pawnKeys) _pawnProviders.Remove(key);
        }

        // ── Settings / Toggle / Cooldown ──────────────────────────────────────

        public static void RegisterSettingsTab(string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)
            => _settingsTabs.Add((tabId, labelFn, drawFn));

        public static IReadOnlyList<(string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)>
            SettingsTabs => _settingsTabs;

        public static void RegisterToggleBehavior(string id, Func<bool> isActive, Action toggle)
            => _toggleBehaviors.Add((id, isActive, toggle));

        public static bool IsAnyToggleActive()
            => _toggleBehaviors.Count > 0 && _toggleBehaviors.Any(b => b.isActive());

        public static void ToggleAll()
        {
            foreach (var (_, _, toggle) in _toggleBehaviors)
                toggle();
        }

        public static bool HasToggleBehaviors => _toggleBehaviors.Count > 0;

        public static void RegisterModCooldown(string modId, Func<int> getCooldownTicks)
            => _modCooldownGetters[modId] = getCooldownTicks;

        public static Func<int>? GetModCooldownGetter(string modId)
            => _modCooldownGetters.TryGetValue(modId, out var getter) ? getter : null;

        public static IReadOnlyDictionary<string, Func<int>> ModCooldownGetters => _modCooldownGetters;

        public static void RegisterDialogueTrigger(Action<Pawn, string, Pawn?> triggerFn)
        {
            _dialogueTriggerFn = triggerFn;
        }

        public static bool CanTriggerDialogue => _dialogueTriggerFn != null;

        public static void TriggerDialogue(Pawn pawn, string context, Pawn? recipient = null)
        {
            if (_dialogueTriggerFn == null)
            {
                Log.Warning("[RimMind] TriggerDialogue called but no dialogue trigger registered.");
                return;
            }
            _dialogueTriggerFn(pawn, context, recipient);
        }

        // ── SkipCheck API ──────────────────────────────────────────────────

        public static void RegisterDialogueSkipCheck(string sourceId, Func<Pawn, string, bool> skipCheck)
        {
            _dialogueSkipChecks[sourceId] = skipCheck;
        }

        public static void UnregisterDialogueSkipCheck(string sourceId)
            => _dialogueSkipChecks.Remove(sourceId);

        public static bool ShouldSkipDialogue(Pawn pawn, string triggerType)
        {
            foreach (var kvp in _dialogueSkipChecks.Values.ToList())
            {
                try
                {
                    if (kvp(pawn, triggerType)) return true;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMind] DialogueSkipCheck error: {ex.Message}");
                }
            }
            return false;
        }

        public static void RegisterFloatMenuSkipCheck(string sourceId, Func<bool> skipCheck)
        {
            _floatMenuSkipChecks[sourceId] = skipCheck;
        }

        public static void UnregisterFloatMenuSkipCheck(string sourceId)
            => _floatMenuSkipChecks.Remove(sourceId);

        public static bool ShouldSkipFloatMenu()
        {
            foreach (var check in _floatMenuSkipChecks.Values.ToList())
            {
                try
                {
                    if (check()) return true;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMind] FloatMenuSkipCheck error: {ex.Message}");
                }
            }
            return false;
        }

        // ── ActionSkipCheck API ──────────────────────────────────────────────

        public static void RegisterActionSkipCheck(string sourceId, Func<string, bool> skipCheck)
        {
            _actionSkipChecks[sourceId] = skipCheck;
        }

        public static void UnregisterActionSkipCheck(string sourceId)
            => _actionSkipChecks.Remove(sourceId);

        public static bool ShouldSkipAction(string intentId)
        {
            foreach (var check in _actionSkipChecks.Values.ToList())
            {
                try
                {
                    if (check(intentId)) return true;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMind] ActionSkipCheck error: {ex.Message}");
                }
            }
            return false;
        }

        // ── Incident Cooldown API ────────────────────────────────────────────

        private static readonly List<Action> _incidentExecutedCallbacks = new List<Action>();

        public static void RegisterIncidentExecutedCallback(Action callback)
        {
            _incidentExecutedCallbacks.Add(callback);
        }

        public static void NotifyIncidentExecuted()
        {
            foreach (var cb in _incidentExecutedCallbacks.ToList())
            {
                try { cb(); }
                catch (System.Exception ex) { Log.Warning($"[RimMind] IncidentExecuted callback error: {ex.Message}"); }
            }
        }

        public static void UnregisterIncidentExecutedCallback(Action callback)
        {
            _incidentExecutedCallbacks.Remove(callback);
        }

        private static readonly List<Func<bool>> _storytellerIncidentSkipChecks = new List<Func<bool>>();

        public static void RegisterStorytellerIncidentSkipCheck(Func<bool> check)
        {
            _storytellerIncidentSkipChecks.Add(check);
        }

        public static void UnregisterStorytellerIncidentSkipCheck(Func<bool> check)
        {
            _storytellerIncidentSkipChecks.Remove(check);
        }

        public static bool ShouldSkipStorytellerIncident()
        {
            foreach (var check in _storytellerIncidentSkipChecks.ToList())
            {
                try { if (check()) return true; }
                catch (System.Exception ex) { Log.Warning($"[RimMind] StorytellerIncidentSkipCheck error: {ex.Message}"); }
            }
            return false;
        }

        // ── RequestOverlay API ────────────────────────────────────────────────

        public static void RegisterPendingRequest(RequestEntry entry)
            => RequestOverlay.Register(entry);

        public static IReadOnlyList<RequestEntry> GetPendingRequests()
            => RequestOverlay.Pending;

        public static void RemovePendingRequest(RequestEntry entry)
            => RequestOverlay.Remove(entry);

        // ── 内部 ──────────────────────────────────────────────────────────────

        private static IAIClient? GetClient()
        {
            var s = RimMindCoreMod.Settings;
            if (!s.IsConfigured()) return null;
            return new OpenAIClient(s);
        }
    }
}
