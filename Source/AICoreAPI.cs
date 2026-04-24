using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Core.Agent;
using RimMind.Core.AgentBus;
using RimMind.Core.Client;
using RimMind.Core.Client.OpenAI;
using RimMind.Core.Client.Player2;
using RimMind.Core.Context;
using RimMind.Core.Extensions;
using RimMind.Core.Flywheel;
using RimMind.Core.Internal;
using RimMind.Core.Npc;
using RimMind.Core.Prompt;
using RimMind.Core.Settings;
using RimMind.Core.UI;
using RimWorld;
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

        private static IAgentProvider? _agentProvider;
        private static IEventBus? _eventBus;

        private static readonly HistoryManager _historyManager = HistoryManager.Instance;
        private static readonly ContextEngine _contextEngine = new ContextEngine(_historyManager);
        internal static HistoryManager GetHistoryManager() => _historyManager;
        public static ContextEngine GetContextEngine() => _contextEngine;
        public static FlywheelTelemetryCollector Telemetry { get; } = new FlywheelTelemetryCollector();

        // ── 核心请求 API ──────────────────────────────────────────────────────

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

        // ── NPC Chat API ─────────────────────────────────────────────────────

        public static async Task<NpcChatResult> Chat(ContextRequest request, CancellationToken ct = default)
        {
            var snapshot = _contextEngine.BuildSnapshot(request);
            var driver = StorageDriverFactory.GetDriver();
            return await driver.ChatAsync(snapshot, ct);
        }

        // ── Structured Request API ───────────────────────────────────────────

        public static void RequestStructuredAsync(AIRequest request, string? jsonSchema, Action<AIResponse> onComplete, List<StructuredTool>? tools = null)
        {
            var s = RimMindCoreMod.Settings;
            if (s == null || !s.IsConfigured())
            {
                onComplete?.Invoke(AIResponse.Failure(request.RequestId, "AI client not configured."));
                return;
            }

            if (s.provider == AIProvider.Player2)
            {
                var p2Client = EnsurePlayer2Client(s);
                if (p2Client != null && p2Client.IsConfigured())
                {
                    Task.Run(async () =>
                    {
                        var response = await p2Client.SendStructuredAsync(request, jsonSchema, tools);
                        LongEventHandler.ExecuteWhenFinished(() => onComplete?.Invoke(response));
                    });
                    return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        var client = await Player2Client.CreateAsync(s);
                        if (client?.IsConfigured() == true)
                        {
                            lock (_player2Lock)
                            {
                                _cachedPlayer2Client = client;
                                _cachedProvider = AIProvider.Player2;
                            }
                            var response = await client.SendStructuredAsync(request, jsonSchema, tools);
                            LongEventHandler.ExecuteWhenFinished(() => onComplete?.Invoke(response));
                        }
                        else
                        {
                            LongEventHandler.ExecuteWhenFinished(() =>
                                onComplete?.Invoke(AIResponse.Failure(request.RequestId, "Player2 client not configured.")));
                        }
                    }
                    catch (Exception ex)
                    {
                        AIRequestQueue.LogFromBackground($"[RimMind] Player2 structured request failed: {ex.Message}", isWarning: true);
                        LongEventHandler.ExecuteWhenFinished(() =>
                            onComplete?.Invoke(AIResponse.Failure(request.RequestId, ex.Message)));
                    }
                });
                return;
            }

            var openAIClient = new OpenAIClient(s);
            if (openAIClient.IsConfigured())
            {
                Task.Run(async () =>
                {
                    var response = await openAIClient.SendStructuredAsync(request, jsonSchema, tools);
                    LongEventHandler.ExecuteWhenFinished(() => onComplete?.Invoke(response));
                });
                return;
            }

            onComplete?.Invoke(AIResponse.Failure(request.RequestId, "AI client not configured."));
        }

        public static ContextSnapshot BuildContextSnapshot(ContextRequest request)
        {
            return _contextEngine.BuildSnapshot(request);
        }

        public static void RequestStructured(ContextRequest request, string schema,
            Action<AIResponse> onComplete, List<StructuredTool>? tools = null)
        {
            var snapshot = _contextEngine.BuildSnapshot(request);
            var aiRequest = new AIRequest
            {
                SystemPrompt = null,
                Messages = snapshot.Messages,
                MaxTokens = snapshot.MaxTokens,
                Temperature = snapshot.Temperature,
                RequestId = $"Structured_{request.NpcId}",
                ModId = request.Scenario.ToString(),
                ExpireAtTicks = Find.TickManager.TicksGame + 600,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            Action<AIResponse> wrappedOnComplete = (response) =>
            {
                try
                {
                    Telemetry.Record(new TelemetryRecord
                    {
                        NpcId = request.NpcId,
                        Scenario = request.Scenario,
                        PromptTokens = response.PromptTokens,
                        CompletionTokens = response.CompletionTokens,
                        TotalTokens = response.TokensUsed,
                        CachedTokens = response.CachedTokens,
                        BudgetValue = snapshot.BudgetValue,
                        KeysIncluded = snapshot.IncludedKeys,
                        KeysTrimmed = snapshot.TrimmedKeys,
                        LayerTokenBreakdown = new Dictionary<string, int>
                        {
                            { "L0", snapshot.Meta.L0Tokens },
                            { "L1", snapshot.Meta.L1Tokens },
                            { "L2", snapshot.Meta.L2Tokens },
                            { "L3", snapshot.Meta.L3Tokens },
                            { "L4", snapshot.Meta.L4Tokens },
                        },
                        TimestampTicks = DateTime.Now.Ticks,
                    });
                }
                catch { }
                onComplete?.Invoke(response);
            };

            try
            {
                RequestStructuredAsync(aiRequest, schema, wrappedOnComplete, tools);
            }
            catch
            {
                var fallbackRequest = new AIRequest
                {
                    SystemPrompt = null,
                    Messages = snapshot.Messages,
                    MaxTokens = snapshot.MaxTokens,
                    Temperature = snapshot.Temperature,
                    RequestId = aiRequest.RequestId,
                    ModId = aiRequest.ModId,
                    ExpireAtTicks = aiRequest.ExpireAtTicks,
                    UseJsonMode = true,
                    Priority = aiRequest.Priority,
                };
                var queue = AIRequestQueue.Instance;
                var client = GetClient();
                if (queue != null && client != null)
                    queue.Enqueue(fallbackRequest, wrappedOnComplete, client);
                else
                    wrappedOnComplete?.Invoke(AIResponse.Failure(fallbackRequest.RequestId, "No AI client available"));
            }
        }

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
            float priorityFloat = 1.0f - (priority / 10.0f);
            ContextLayer layer = InferLayer(priority);
            var wrappedProvider = new Func<Pawn, List<ContextEntry>>(_ =>
            {
                string? val = provider();
                return string.IsNullOrEmpty(val) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(val) };
            });
            ContextKeyRegistry.Register(category, layer, priorityFloat, wrappedProvider, modId);
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
            float priorityFloat = 1.0f - (priority / 10.0f);
            ContextLayer layer = InferLayer(priority);
            var wrappedProvider = new Func<Pawn, List<ContextEntry>>(pawn =>
            {
                string val = provider(pawn.Name?.ToStringShort ?? pawn.LabelShort);
                return string.IsNullOrEmpty(val) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(val) };
            });
            ContextKeyRegistry.Register(category, layer, priorityFloat, wrappedProvider, modId);
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
            float priorityFloat = 1.0f - (priority / 10.0f);
            ContextLayer layer = InferLayer(priority);
            var wrappedProvider = new Func<Pawn, List<ContextEntry>>(pawn =>
            {
                string? val = provider(pawn);
                return string.IsNullOrEmpty(val) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(val) };
            });
            ContextKeyRegistry.Register(category, layer, priorityFloat, wrappedProvider, modId);
        }

        // ── Provider 查询（供外部 Mod 读取 RimMind 数据） ──────────────────────

        public static string? GetProviderData(string category, Pawn pawn)
        {
            if (!_pawnProviders.TryGetValue(category, out var entry)) return null;
            try { return entry.provider(pawn); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static string? GetStaticProviderData(string category)
        {
            if (!_staticProviders.TryGetValue(category, out var entry)) return null;
            try { return entry.provider(); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetStaticProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static string? GetDynamicProviderData(string category, string query)
        {
            if (!_dynamicProviders.TryGetValue(category, out var entry)) return null;
            try { return entry.provider(query); }
            catch (System.Exception ex) { Log.Warning($"[RimMind] GetDynamicProviderData '{category}' error: {ex.Message}"); return null; }
        }

        public static List<string> GetRegisteredCategories()
        {
            var all = new HashSet<string>();
            all.UnionWith(_staticProviders.Keys);
            all.UnionWith(_pawnProviders.Keys);
            all.UnionWith(_dynamicProviders.Keys);

            return all.ToList();
        }

        // ── Provider 卸载 ──────────────────────────────────────────────────────

        public static void UnregisterPawnContextProvider(string category)
        {
            _pawnProviders.Remove(category);
            ContextKeyRegistry.Unregister(category);
        }

        public static void UnregisterStaticProvider(string category)
        {
            _staticProviders.Remove(category);
            ContextKeyRegistry.Unregister(category);
        }

        public static void UnregisterDynamicProvider(string category)
        {
            _dynamicProviders.Remove(category);
            ContextKeyRegistry.Unregister(category);
        }

        public static void UnregisterModProviders(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            var staticKeys = _staticProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in staticKeys) { _staticProviders.Remove(key); ContextKeyRegistry.Unregister(key); }

            var dynamicKeys = _dynamicProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in dynamicKeys) { _dynamicProviders.Remove(key); ContextKeyRegistry.Unregister(key); }

            var pawnKeys = _pawnProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in pawnKeys) { _pawnProviders.Remove(key); ContextKeyRegistry.Unregister(key); }
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

        // ── AgentProvider / EventBus API ──────────────────────────────────────

        public static IAgentProvider GetAgentProvider()
            => _agentProvider ?? new DefaultAgentProvider();

        public static void RegisterAgentProvider(IAgentProvider provider)
            => _agentProvider = provider;

        public static IEventBus GetEventBus()
            => _eventBus ?? new EventBusAdapter();

        public static void RegisterEventBus(IEventBus eventBus)
            => _eventBus = eventBus;

        // ── AgentIdentity Provider API ──────────────────────────────────────

        private static Func<Pawn, AgentIdentity?>? _agentIdentityProvider;

        public static void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider)
            => _agentIdentityProvider = provider;

        public static AgentIdentity? GetAgentIdentity(Pawn pawn)
            => _agentIdentityProvider?.Invoke(pawn);

        // ── AgentAction Bridge API ─────────────────────────────────────────

        private static IAgentActionBridge? _agentActionBridge;

        public static void RegisterAgentActionBridge(IAgentActionBridge bridge)
            => _agentActionBridge = bridge;

        public static IAgentActionBridge? GetAgentActionBridge()
            => _agentActionBridge;

        // ── AudioPlayer API ──────────────────────────────────────────────

        private static IAudioPlayer _audioPlayer = new NullAudioPlayer();

        public static void RegisterAudioPlayer(IAudioPlayer player)
            => _audioPlayer = player ?? new NullAudioPlayer();

        public static IAudioPlayer AudioPlayer => _audioPlayer;

        // ── Perception API ──────────────────────────────────────────────────

        public static void PublishPerception(int pawnId, string type, string content, float importance = 0.5f)
            => Perception.PerceptionBridge.PublishPerception(pawnId, type, content, importance);

        public static void PublishBroadcastPerception(string type, string content, float importance = 0.5f, Verse.Map? map = null)
            => Perception.PerceptionBridge.PublishBroadcast(type, content, importance, map);

        // ── RequestOverlay API ────────────────────────────────────────────────

        public static void RegisterPendingRequest(RequestEntry entry)
            => RequestOverlay.Register(entry);

        public static IReadOnlyList<RequestEntry> GetPendingRequests()
            => RequestOverlay.Pending;

        public static void RemovePendingRequest(RequestEntry entry)
            => RequestOverlay.Remove(entry);

        // ── 内部 ──────────────────────────────────────────────────────────────

        private static ContextLayer InferLayer(int priority)
        {
            if (priority <= 1) return ContextLayer.L0_Static;
            if (priority <= 3) return ContextLayer.L1_Baseline;
            if (priority <= 5) return ContextLayer.L2_Environment;
            return ContextLayer.L3_State;
        }

        internal static IAIClient? GetClient()
        {
            var s = RimMindCoreMod.Settings;
            if (!s.IsConfigured()) return null;

            if (s.provider == AIProvider.Player2)
            {
                var cached = EnsurePlayer2Client(s);
                return cached;
            }

            return new OpenAIClient(s);
        }

        private static Player2Client? _cachedPlayer2Client;
        private static AIProvider _cachedProvider;
        private static readonly object _player2Lock = new object();

        private static Player2Client? EnsurePlayer2Client(RimMindCoreSettings s)
        {
            lock (_player2Lock)
            {
                if (_cachedPlayer2Client != null && _cachedProvider == AIProvider.Player2)
                    return _cachedPlayer2Client.IsConfigured() ? _cachedPlayer2Client : null;
                return null;
            }
        }

        public static void InvalidateClientCache()
        {
            lock (_player2Lock)
            {
                if (_cachedProvider == AIProvider.Player2)
                    Player2Client.StopHealthCheck();
                _cachedPlayer2Client = null;
                _cachedProvider = default;
            }
        }

        public static Player2Client? GetPlayer2Client()
        {
            lock (_player2Lock)
            {
                return (_cachedPlayer2Client != null && _cachedPlayer2Client.IsConfigured())
                    ? _cachedPlayer2Client : null;
            }
        }

        private static readonly List<IParameterTuner> _parameterTuners = new List<IParameterTuner>();
        private static readonly List<ISensorProvider> _sensorProviders = new List<ISensorProvider>();
        private static readonly List<IAgentModeProvider> _agentModeProviders = new List<IAgentModeProvider>();
        private static readonly List<IStreamingResponseHandler> _streamingHandlers = new List<IStreamingResponseHandler>();

        public static void RegisterParameterTuner(IParameterTuner tuner) { _parameterTuners.Add(tuner); }
        public static void UnregisterParameterTuner(string tunerId) { _parameterTuners.RemoveAll(t => t.TunerId == tunerId); }
        public static IReadOnlyList<IParameterTuner> ParameterTuners => _parameterTuners;

        public static void RegisterSensorProvider(ISensorProvider provider) { _sensorProviders.Add(provider); }
        public static void UnregisterSensorProvider(string sensorId) { _sensorProviders.RemoveAll(s => s.SensorId == sensorId); }
        public static IReadOnlyList<ISensorProvider> SensorProviders => _sensorProviders;

        public static void RegisterAgentModeProvider(IAgentModeProvider provider) { _agentModeProviders.Add(provider); }
        public static void UnregisterAgentModeProvider(string providerId) { _agentModeProviders.RemoveAll(p => p.ProviderId == providerId); }
        public static IReadOnlyList<IAgentModeProvider> AgentModeProviders => _agentModeProviders;

        public static bool IsPawnAgentControlled(Pawn pawn) => _agentModeProviders.Any(p => p.IsAgentControlled(pawn));

        public static void RegisterStreamingHandler(IStreamingResponseHandler handler) { _streamingHandlers.Add(handler); }
        public static void UnregisterStreamingHandler(string handlerId) { _streamingHandlers.RemoveAll(h => h.HandlerId == handlerId); }
        public static IReadOnlyList<IStreamingResponseHandler> StreamingHandlers => _streamingHandlers;
    }
}
