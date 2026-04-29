using System;
using System.Collections.Concurrent;
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

        private static readonly ConcurrentDictionary<string, (string modId, Func<string?> provider, int priority)>
            _staticProviders = new ConcurrentDictionary<string, (string, Func<string?>, int)>();

        private static readonly ConcurrentDictionary<string, (string modId, Func<Pawn, string?> provider, int priority)>
            _pawnProviders = new ConcurrentDictionary<string, (string, Func<Pawn, string?>, int)>();

        private static readonly ConcurrentDictionary<string, (string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)>
            _settingsTabs = new ConcurrentDictionary<string, (string, Func<string>, Action<UnityEngine.Rect>)>();

        private static readonly ConcurrentDictionary<string, (string id, Func<bool> isActive, Action toggle)>
            _toggleBehaviors = new ConcurrentDictionary<string, (string, Func<bool>, Action)>();

        private static readonly ConcurrentDictionary<string, Func<int>> _modCooldownGetters
            = new ConcurrentDictionary<string, Func<int>>();

        private static volatile Action<Pawn, string, Pawn?>? _dialogueTriggerFn;

        private static readonly ConcurrentDictionary<string, Func<Pawn, string, bool>> _dialogueSkipChecks
            = new ConcurrentDictionary<string, Func<Pawn, string, bool>>();

        private static readonly ConcurrentDictionary<string, Func<bool>> _floatMenuSkipChecks
            = new ConcurrentDictionary<string, Func<bool>>();

        private static readonly ConcurrentDictionary<string, Func<string, bool>> _actionSkipChecks
            = new ConcurrentDictionary<string, Func<string, bool>>();

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
            var driver = StorageDriverFactory.GetDriver();

            if (!driver.IsNpcAlive(request.NpcId) && request.NpcId.StartsWith("NPC-"))
            {
                if (int.TryParse(request.NpcId.Substring(4), out int thingId))
                {
                    var pawn = NpcManager.FindPawnByNpcId(request.NpcId);
                    if (pawn != null)
                    {
                        var profile = NpcProfileBuilder.BuildPawnNpc(pawn);
                        await driver.SpawnNpcAsync(profile);
                        NpcManager.Instance?.SpawnNpc(profile);
                    }
                }
            }

            var snapshot = _contextEngine.BuildSnapshot(request);
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
                SystemPrompt = string.Empty,
                Messages = snapshot.Messages,
                MaxTokens = snapshot.MaxTokens,
                Temperature = snapshot.Temperature,
                RequestId = $"Structured_{request.NpcId}",
                ModId = request.Scenario.ToString(),
                ExpireAtTicks = Find.TickManager.TicksGame + 30000,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            Action<AIResponse> wrappedOnComplete = (response) =>
            {
                try
                {
                    bool parseSuccess = response.Success && !string.IsNullOrEmpty(response.Content);
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
                            { "L5", snapshot.Meta.L5Tokens },
                        },
                        KeyChangeFreq = snapshot.KeyChangeCounts.Count > 0
                            ? new Dictionary<string, int>(snapshot.KeyChangeCounts) : null,
                        CacheHitRate = null,
                        ScoreDistribution = snapshot.KeyScores.Count > 0
                            ? new Dictionary<string, float>(snapshot.KeyScores) : null,
                        DiffCount = snapshot.DiffCount,
                        LatencyByLayerMs = snapshot.LatencyByLayerMs.Count > 0
                            ? new Dictionary<string, long>(snapshot.LatencyByLayerMs) : null,
                        RequestLatencyMs = snapshot.BuildStartTicks > 0
                            ? (DateTime.Now.Ticks - snapshot.BuildStartTicks) / TimeSpan.TicksPerMillisecond : 0,
                        ResponseParseSuccess = parseSuccess,
                        TimestampTicks = DateTime.Now.Ticks,
                    });
                }
                catch (Exception ex) { Log.Warning($"[RimMind] Telemetry record failed: {ex.Message}"); }
                onComplete?.Invoke(response);
            };

            try
            {
                RequestStructuredAsync(aiRequest, schema, wrappedOnComplete, tools);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] RequestStructuredAsync threw, falling back to queue: {ex.Message}");
                var fallbackRequest = new AIRequest
                {
                    SystemPrompt = string.Empty,
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

        // ── 状态查询 ──────────────────────────────────────────────────────────

        public static bool IsConfigured() => RimMindCoreMod.Settings.IsConfigured();

        // ── Provider 注册（去重/覆盖） ──────────────────────────────────────────

        [Obsolete("Use ContextKeyRegistry.Register instead")]
        public static void RegisterStaticProvider(string category, Func<string?> provider,
            int priority = PromptSection.PriorityAuxiliary, string modId = "", bool overrideExisting = true)
        {
            if (_staticProviders.ContainsKey(category) && !overrideExisting) return;
            _staticProviders[category] = (modId, provider, priority);
            float priorityFloat = 1.0f - (priority / 10.0f);
            ContextLayer layer = InferLayer(priority);
            var wrappedProvider = new Func<Pawn, List<ContextEntry>>(_ =>
            {
                string? val = provider();
                return string.IsNullOrEmpty(val) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(val!) };
            });
            ContextKeyRegistry.Register(category, layer, priorityFloat, wrappedProvider, modId);
        }

        [Obsolete("Use ContextKeyRegistry.RegisterPawnContext instead")]
        public static void RegisterPawnContextProvider(string category, Func<Pawn, string?> provider,
            int priority = PromptSection.PriorityAuxiliary, string modId = "", bool overrideExisting = true)
        {
            if (_pawnProviders.ContainsKey(category) && !overrideExisting) return;
            _pawnProviders[category] = (modId, provider, priority);
            float priorityFloat = 1.0f - (priority / 10.0f);
            ContextLayer layer = InferLayer(priority);
            var wrappedProvider = new Func<Pawn, List<ContextEntry>>(pawn =>
            {
                string? val = provider(pawn);
                return string.IsNullOrEmpty(val) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(val!) };
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

        public static List<string> GetRegisteredCategories()
        {
            var all = new HashSet<string>();
            all.UnionWith(_staticProviders.Keys);
            all.UnionWith(_pawnProviders.Keys);
            return all.ToList();
        }

        // ── Provider 卸载 ──────────────────────────────────────────────────────

        public static void UnregisterPawnContextProvider(string category)
        {
            _pawnProviders.TryRemove(category, out _);
            ContextKeyRegistry.Unregister(category);
        }

        public static void UnregisterStaticProvider(string category)
        {
            _staticProviders.TryRemove(category, out _);
            ContextKeyRegistry.Unregister(category);
        }

        public static void UnregisterModProviders(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            var staticKeys = _staticProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in staticKeys) { _staticProviders.TryRemove(key, out _); ContextKeyRegistry.Unregister(key); }

            var pawnKeys = _pawnProviders.Where(kvp => kvp.Value.modId == modId).Select(kvp => kvp.Key).ToList();
            foreach (var key in pawnKeys) { _pawnProviders.TryRemove(key, out _); ContextKeyRegistry.Unregister(key); }
        }

        // ── Settings / Toggle / Cooldown ──────────────────────────────────────

        public static void RegisterSettingsTab(string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)
            => _settingsTabs[tabId] = (tabId, labelFn, drawFn);

        public static IReadOnlyList<(string tabId, Func<string> labelFn, Action<UnityEngine.Rect> drawFn)>
            SettingsTabs => _settingsTabs.Values.ToList();

        public static void RegisterToggleBehavior(string id, Func<bool> isActive, Action toggle)
            => _toggleBehaviors[id] = (id, isActive, toggle);

        public static bool IsAnyToggleActive()
            => _toggleBehaviors.Values.Any(b => b.isActive());

        public static void ToggleAll()
        {
            foreach (var (_, _, toggle) in _toggleBehaviors.Values.ToList())
                toggle();
        }

        public static bool HasToggleBehaviors => _toggleBehaviors.Count > 0;

        public static void RegisterModCooldown(string modId, Func<int> getCooldownTicks)
            => _modCooldownGetters[modId] = getCooldownTicks;

        public static Func<int>? GetModCooldownGetter(string modId)
            => _modCooldownGetters.TryGetValue(modId, out var getter) ? getter : null;

        public static ConcurrentDictionary<string, Func<int>> ModCooldownGetters => _modCooldownGetters;

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
            => _dialogueSkipChecks.TryRemove(sourceId, out _);

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
            => _floatMenuSkipChecks.TryRemove(sourceId, out _);

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
            => _actionSkipChecks.TryRemove(sourceId, out _);

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

        private static readonly ConcurrentDictionary<string, Action> _incidentExecutedCallbacks
            = new ConcurrentDictionary<string, Action>();

        public static void RegisterIncidentExecutedCallback(Action callback)
        {
            string key = $"cb_{callback.Method.DeclaringType?.Name}_{callback.Method.Name}_{System.Threading.Interlocked.Increment(ref _callbackCounter)}";
            _incidentExecutedCallbacks[key] = callback;
        }

        public static void NotifyIncidentExecuted()
        {
            foreach (var cb in _incidentExecutedCallbacks.Values.ToList())
            {
                try { cb(); }
                catch (System.Exception ex) { Log.Warning($"[RimMind] IncidentExecuted callback error: {ex.Message}"); }
            }
        }

        public static void UnregisterIncidentExecutedCallback(Action callback)
        {
            var key = _incidentExecutedCallbacks.FirstOrDefault(kvp => kvp.Value == callback).Key;
            if (key != null) _incidentExecutedCallbacks.TryRemove(key, out _);
        }

        private static readonly ConcurrentDictionary<string, Func<bool>> _storytellerIncidentSkipChecks
            = new ConcurrentDictionary<string, Func<bool>>();

        public static void RegisterStorytellerIncidentSkipCheck(Func<bool> check)
        {
            string key = $"sc_{System.Threading.Interlocked.Increment(ref _callbackCounter)}";
            _storytellerIncidentSkipChecks[key] = check;
        }

        public static void UnregisterStorytellerIncidentSkipCheck(Func<bool> check)
        {
            var key = _storytellerIncidentSkipChecks.FirstOrDefault(kvp => kvp.Value == check).Key;
            if (key != null) _storytellerIncidentSkipChecks.TryRemove(key, out _);
        }

        public static bool ShouldSkipStorytellerIncident()
        {
            foreach (var check in _storytellerIncidentSkipChecks.Values.ToList())
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

        private static volatile Func<Pawn, AgentIdentity?>? _agentIdentityProvider;

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

        // ── RequestOverlay API ────────────────────────────────────────────────

        public static void RegisterPendingRequest(RequestEntry entry)
            => RequestOverlay.Register(entry);

        public static IReadOnlyList<RequestEntry> GetPendingRequests()
            => RequestOverlay.Pending;

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
            OpenAIClient.InvalidateFormatCache();
        }

        public static Player2Client? GetPlayer2Client()
        {
            lock (_player2Lock)
            {
                return (_cachedPlayer2Client != null && _cachedPlayer2Client.IsConfigured())
                    ? _cachedPlayer2Client : null;
            }
        }

        private static readonly ConcurrentDictionary<string, IParameterTuner> _parameterTuners
            = new ConcurrentDictionary<string, IParameterTuner>();
        private static readonly ConcurrentDictionary<string, ISensorProvider> _sensorProviders
            = new ConcurrentDictionary<string, ISensorProvider>();
        private static readonly ConcurrentDictionary<string, IAgentModeProvider> _agentModeProviders
            = new ConcurrentDictionary<string, IAgentModeProvider>();
        private static readonly ConcurrentDictionary<string, IStreamingResponseHandler> _streamingHandlers
            = new ConcurrentDictionary<string, IStreamingResponseHandler>();
        private static int _callbackCounter;

        public static void RegisterParameterTuner(IParameterTuner tuner) { _parameterTuners[tuner.TunerId] = tuner; }
        public static void UnregisterParameterTuner(string tunerId) { _parameterTuners.TryRemove(tunerId, out _); }
        public static IReadOnlyList<IParameterTuner> ParameterTuners => _parameterTuners.Values.ToList();

        public static void RegisterSensorProvider(ISensorProvider provider) { _sensorProviders[provider.SensorId] = provider; RegisterSensorContextKey(provider); }
        public static void UnregisterSensorProvider(string sensorId) { _sensorProviders.TryRemove(sensorId, out _); ContextKeyRegistry.Unregister($"sensor_{sensorId}"); }
        public static IReadOnlyList<ISensorProvider> SensorProviders => _sensorProviders.Values.ToList();

        /// <summary>
        /// Get all Agent Tools from registered sensors for the given pawn.
        /// These can be injected into AI tool-calling requests.
        /// </summary>
        public static List<StructuredTool> GetAgentTools(Pawn pawn)
        {
            var mgr = Sensor.SensorManager.Instance;
            return mgr?.BuildAgentTools(pawn) ?? new List<StructuredTool>();
        }

        public static void RegisterAgentModeProvider(IAgentModeProvider provider) { _agentModeProviders[provider.ProviderId] = provider; }
        public static void UnregisterAgentModeProvider(string providerId) { _agentModeProviders.TryRemove(providerId, out _); }
        public static IReadOnlyList<IAgentModeProvider> AgentModeProviders => _agentModeProviders.Values.ToList();

        public static bool IsPawnAgentControlled(Pawn pawn) => _agentModeProviders.Values.Any(p => p.IsAgentControlled(pawn));

        public static void RegisterStreamingHandler(IStreamingResponseHandler handler) { _streamingHandlers[handler.HandlerId] = handler; }
        public static void UnregisterStreamingHandler(string handlerId) { _streamingHandlers.TryRemove(handlerId, out _); }
        public static IReadOnlyList<IStreamingResponseHandler> StreamingHandlers => _streamingHandlers.Values.ToList();

        public static void ClearModCooldown(string modId) => AIRequestQueue.Instance?.ClearCooldown(modId);

        private static void RegisterSensorContextKey(ISensorProvider sensor)
        {
            string key = $"sensor_{sensor.SensorId}";
            var captured = sensor;
            ContextKeyRegistry.Register(key, ContextLayer.L5_Sensor, captured.Priority / 100f,
                pawn =>
                {
                    string? data = captured.Sense(pawn);
                    if (string.IsNullOrEmpty(data))
                        return new List<ContextEntry>();
                    return new List<ContextEntry> { new ContextEntry(data!) };
                }, "Core");
        }
    }
}
