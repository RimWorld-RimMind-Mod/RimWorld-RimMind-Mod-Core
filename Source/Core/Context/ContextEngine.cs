using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Core.Client;
using RimMind.Core.Flywheel;
using RimMind.Core.Npc;
using RimMind.Core.Prompt;
using RimMind.Core.Settings;
using RimWorld;
using Verse;

namespace RimMind.Core.Context
{
    public class ContextEngine
    {
        private const int MaxCacheEntries = 100;
        private readonly object _sync = new object();

        private readonly BudgetScheduler _scheduler = new BudgetScheduler();
        private readonly HistoryManager _historyManager;
        private readonly EmbedCache _embedCache = new EmbedCache();

        private readonly Dictionary<string, ChatMessage> _l0Cache = new Dictionary<string, ChatMessage>();
        private readonly Dictionary<string, Dictionary<string, string>> _l1BlockCache = new Dictionary<string, Dictionary<string, string>>();
        private readonly Dictionary<string, int> _l1Version = new Dictionary<string, int>();
        private readonly Dictionary<string, Dictionary<string, int>> _l1KeyVersions = new Dictionary<string, Dictionary<string, int>>();
        private readonly Dictionary<string, List<ContextDiff>> _diffStore = new Dictionary<string, List<ContextDiff>>();
        private readonly Dictionary<string, Dictionary<string, string>> _keyLastValues = new Dictionary<string, Dictionary<string, string>>();
        private readonly LinkedList<string> _cacheOrder = new LinkedList<string>();
        private readonly Dictionary<string, bool> _pendingCacheEvents = new Dictionary<string, bool>();
        private readonly EmbeddingSnapshotStore _embeddingSnapshotStore = new EmbeddingSnapshotStore();

        public ContextEngine(HistoryManager historyManager)
        {
            _historyManager = historyManager;
        }

        public BudgetScheduler GetScheduler() => _scheduler;
        public EmbeddingSnapshotStore GetEmbeddingSnapshotStore() => _embeddingSnapshotStore;

        private void TouchCache(string cacheKey)
        {
            _cacheOrder.Remove(cacheKey);
            _cacheOrder.AddLast(cacheKey);
            while (_cacheOrder.Count > MaxCacheEntries)
            {
                var oldest = _cacheOrder.First.Value;
                _cacheOrder.RemoveFirst();
                _l0Cache.Remove(oldest);
                string oldestNpc = oldest.Contains("_") ? oldest.Substring(0, oldest.LastIndexOf('_')) : oldest;
                _l1BlockCache.Remove(oldestNpc);
                _l1Version.Remove(oldestNpc);
                _l1KeyVersions.Remove(oldestNpc);
                _diffStore.Remove(oldestNpc);
                _keyLastValues.Remove(oldestNpc);
                _embedCache.InvalidateNpc(oldestNpc);
                SemanticEmbedding.InvalidateNpc(oldestNpc);
            }
        }

        private void RemoveL0CacheForNpc(string npcId)
        {
            var keysToRemove = new List<string>();
            foreach (var key in _l0Cache.Keys)
            {
                if (key == npcId || key.StartsWith(npcId + "_"))
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
            {
                _l0Cache.Remove(key);
                _cacheOrder.Remove(key);
            }
        }

        public ContextSnapshot BuildSnapshot(ContextRequest request)
        {
            lock (_sync)
            {
                return BuildSnapshotCore(request);
            }
        }

        private ContextSnapshot BuildSnapshotCore(ContextRequest request)
        {
            ScenarioRegistry.RegisterCoreScenarios();
            RelevanceTable.RegisterCoreRelevance();
            ContextKeyRegistry.RegisterCoreKeys();
            string scenario = request.Scenario ?? ScenarioIds.Dialogue;
            string l0CacheKey = $"{request.NpcId}_{scenario}";
            TouchCache(l0CacheKey);
            ContextKeyRegistry.CurrentScenario = scenario;

            try
            {
                var snapshot = new ContextSnapshot
                {
                    NpcId = request.NpcId,
                Scenario = scenario,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                CurrentQuery = request.CurrentQuery,
                BuildStartTicks = DateTime.Now.Ticks,
            };

            var pawn = NpcManager.FindPawnByNpcId(request.NpcId);
            if (pawn == null && request.Map != null)
                pawn = NpcManager.FindProxyPawnForMap(request.Map!);
            var allKeys = ContextKeyRegistry.GetAll();

            var scenarioMeta = ScenarioRegistry.Get(request.Scenario ?? ScenarioIds.Dialogue);

            var excludeSet = new HashSet<string>();
            if (scenarioMeta?.DefaultExcludeKeys != null)
                excludeSet.UnionWith(scenarioMeta.DefaultExcludeKeys);
            if (request.ExcludeKeys != null)
                excludeSet.UnionWith(request.ExcludeKeys);

            var filteredKeys = allKeys.Where(k => !excludeSet.Contains(k.Key)).ToList();

            float budget = request.Budget > 0
                ? request.Budget
                : (scenarioMeta?.DefaultBudget > 0
                    ? scenarioMeta.DefaultBudget
                    : (RimMindCoreMod.Settings?.Context?.ContextBudget > 0
                        ? RimMindCoreMod.Settings.Context.ContextBudget
                        : 0.6f));
            var schedule = _scheduler.Schedule(filteredKeys, request.Scenario ?? ScenarioIds.Dialogue, budget, request.CurrentQuery);

            var allScheduledKeys = schedule.L0Keys.Concat(schedule.L1Keys)
                .Concat(schedule.L2Keys).Concat(schedule.L3Keys).Concat(schedule.L5Keys)
                .Select(k => k.Key).ToArray();
            var scheduledKeySet = new HashSet<string>(allScheduledKeys);
            var trimmedKeyNames = filteredKeys.Where(k => !scheduledKeySet.Contains(k.Key))
                .Select(k => k.Key).ToArray();
            snapshot.IncludedKeys = allScheduledKeys;
            snapshot.TrimmedKeys = trimmedKeyNames;
            snapshot.BudgetValue = budget;

            foreach (var key in schedule.L2Keys.Concat(schedule.L3Keys).Concat(schedule.L5Keys))
            {
                if (key.CurrentScore > 0)
                    snapshot.KeyScores[key.Key] = key.CurrentScore;
            }

            if (_diffStore.TryGetValue(request.NpcId, out var diffs))
                snapshot.DiffCount = diffs.Count;

            var messages = new List<ChatMessage>();

            long l0Start = DateTime.Now.Ticks;
            var l0Msg = BuildL0(request.NpcId, request.Scenario ?? ScenarioIds.Dialogue, schedule.L0Keys, pawn);
            if (l0Msg != null)
            {
                l0Msg.LayerTag = "L0";
                messages.Add(l0Msg);
                snapshot.Meta.L0Tokens = EstimateTokens(l0Msg.Content);
            }
            snapshot.LatencyByLayerMs["L0"] = (DateTime.Now.Ticks - l0Start) / TimeSpan.TicksPerMillisecond;

            long l1Start = DateTime.Now.Ticks;
            var l1Msg = BuildL1(request.NpcId, schedule.L1Keys, pawn);
            if (l1Msg != null)
            {
                l1Msg.LayerTag = "L1";
                messages.Add(l1Msg);
                snapshot.Meta.L1Tokens = EstimateTokens(l1Msg.Content);
            }
            snapshot.LatencyByLayerMs["L1"] = (DateTime.Now.Ticks - l1Start) / TimeSpan.TicksPerMillisecond;

            var l1DiffMsg = BuildDiffMessage(request.NpcId, ContextLayer.L1_Baseline);
            if (l1DiffMsg != null)
            {
                l1DiffMsg.LayerTag = "L1";
                messages.Add(l1DiffMsg);
            }

            long l2Start = DateTime.Now.Ticks;
            var l2Msg = BuildL2L3(schedule.L2Keys, pawn);
            if (l2Msg != null)
            {
                var l2DiffMsg = BuildDiffMessage(request.NpcId, ContextLayer.L2_Environment);
                if (l2DiffMsg != null)
                {
                    l2DiffMsg.LayerTag = "L2";
                    messages.Add(l2DiffMsg);
                }
                l2Msg.LayerTag = "L2";
                messages.Add(l2Msg);
                snapshot.Meta.L2Tokens = EstimateTokens(l2Msg.Content);
            }
            snapshot.LatencyByLayerMs["L2"] = (DateTime.Now.Ticks - l2Start) / TimeSpan.TicksPerMillisecond;

            long l3Start = DateTime.Now.Ticks;
            var l3Msg = BuildL2L3(schedule.L3Keys, pawn);
            if (l3Msg != null)
            {
                var l3DiffMsg = BuildDiffMessage(request.NpcId, ContextLayer.L3_State);
                if (l3DiffMsg != null)
                {
                    l3DiffMsg.LayerTag = "L3";
                    messages.Add(l3DiffMsg);
                }
                l3Msg.LayerTag = "L3";
                messages.Add(l3Msg);
                snapshot.Meta.L3Tokens = EstimateTokens(l3Msg.Content);
            }
            snapshot.LatencyByLayerMs["L3"] = (DateTime.Now.Ticks - l3Start) / TimeSpan.TicksPerMillisecond;

            long l5Start = DateTime.Now.Ticks;
            var l5Msg = BuildL5(schedule.L5Keys, pawn);
            if (l5Msg != null)
            {
                l5Msg.LayerTag = "L5";
                messages.Add(l5Msg);
                snapshot.Meta.L5Tokens = EstimateTokens(l5Msg.Content);
            }
            snapshot.LatencyByLayerMs["L5"] = (DateTime.Now.Ticks - l5Start) / TimeSpan.TicksPerMillisecond;

            int maxRounds = schedule.MaxHistoryRounds;
            var history = _historyManager.GetHistory(request.NpcId, maxRounds, scenario);
            foreach (var (role, content) in history)
            {
                messages.Add(new ChatMessage { Role = role, Content = content });
                snapshot.Meta.L4Tokens += EstimateTokens(content);
            }

            if (!string.IsNullOrEmpty(request.CurrentQuery))
            {
                string queryContent = !string.IsNullOrEmpty(request.SpeakerName)
                    ? "RimMind.Core.Prompt.Dialogue.SpeakerSays".Translate(request.SpeakerName!, request.CurrentQuery!)
                    : request.CurrentQuery!;
                messages.Add(new ChatMessage { Role = "user", Content = queryContent, LayerTag = "L4" });
            }

            bool hasUserMessage = messages.Any(m => m.Role == "user");
            if (!hasUserMessage)
            {
                string scenarioLabel = !string.IsNullOrEmpty(request.Scenario)
                    ? request.Scenario! : "general";
                messages.Add(new ChatMessage { Role = "user", Content = "RimMind.Core.Prompt.AutoAwait".Translate(scenarioLabel) });
            }

            snapshot.Messages = messages;
            snapshot.Meta.TotalTokens = snapshot.Meta.L0Tokens + snapshot.Meta.L1Tokens +
                snapshot.Meta.L2Tokens + snapshot.Meta.L3Tokens + snapshot.Meta.L4Tokens + snapshot.Meta.L5Tokens;
            snapshot.EstimatedTokens = snapshot.Meta.TotalTokens;

            ApplyBudgetTrim(snapshot);

            UpdateKeyValues(request.NpcId, filteredKeys, pawn);

            MergeExpiredDiffs(request.NpcId);

            if (_pendingCacheEvents.Count > 0)
            {
                foreach (var kvp in _pendingCacheEvents)
                    snapshot.CacheHitEvents[kvp.Key] = kvp.Value;
                _pendingCacheEvents.Clear();
            }

            foreach (var key in filteredKeys)
            {
                if (key.UpdateCount > 0)
                    snapshot.KeyChangeCounts[key.Key] = key.UpdateCount;
            }

            try
            {
                var allSnapshotKeys = schedule.L0Keys.Concat(schedule.L1Keys)
                    .Concat(schedule.L2Keys).Concat(schedule.L3Keys).Concat(schedule.L5Keys);
                foreach (var key in allSnapshotKeys)
                {
                    string sourceText = "";
                    if (_keyLastValues.TryGetValue(request.NpcId, out var vals) &&
                        vals.TryGetValue(key.Key, out var val))
                    {
                        sourceText = val.Length > 500
                            ? (char.IsHighSurrogate(val[499]) ? val.Substring(0, 499) : val.Substring(0, 500))
                            : val;
                    }
                    float[]? vector = SemanticEmbedding.GetBlockEmbedding(request.NpcId, key.Key);
                    if (vector == null && key.KeyEmbedding != null)
                        vector = key.KeyEmbedding;
                    float relevanceScore = snapshot.KeyScores.TryGetValue(key.Key, out var score) ? score : 0f;
                    _embeddingSnapshotStore.Record(new EmbeddingSnapshotRecord
                    {
                        NpcId = request.NpcId,
                        ScenarioId = request.Scenario ?? ScenarioIds.Dialogue,
                        Key = key.Key,
                        Layer = key.Layer.ToString(),
                        SourceText = sourceText,
                        Vector = vector!,
                        RelevanceScore = relevanceScore,
                        TimestampTicks = DateTime.Now.Ticks,
                    });
                }
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Embedding snapshot failed: {ex.Message}"); }

                return snapshot;
            }
            finally
            {
                ContextKeyRegistry.CurrentScenario = string.Empty;
            }
        }

        public void InvalidateLayer(string npcId, ContextLayer layer)
        {
            lock (_sync)
            {
                if (layer == ContextLayer.L0_Static)
                    RemoveL0CacheForNpc(npcId);
                if (layer == ContextLayer.L1_Baseline)
                {
                    _l1BlockCache.Remove(npcId);
                    _l1Version.Remove(npcId);
                    _l1KeyVersions.Remove(npcId);
                }
            }
        }

        public void InvalidateKey(string npcId, string key)
        {
            lock (_sync)
            {
                if (_keyLastValues.TryGetValue(npcId, out var dict))
                    dict.Remove(key);
                if (_l1BlockCache.TryGetValue(npcId, out var blocks))
                    blocks.Remove(key);
                _embedCache.InvalidateBlock(npcId, key);
                _embedCache.InvalidateEntries(npcId, key);
                SemanticEmbedding.InvalidateBlockEmbedding(npcId, key);
                SemanticEmbedding.InvalidateEntryEmbeddings(npcId, key);
            }
        }

        public void UpdateBaseline(string npcId)
        {
            lock (_sync)
            {
                _l1BlockCache.Remove(npcId);
                _l1Version.Remove(npcId);
                _l1KeyVersions.Remove(npcId);
                if (_diffStore.TryGetValue(npcId, out var diffs))
                    diffs.Clear();
            }
        }

        public void InvalidateNpc(string npcId)
        {
            lock (_sync)
            {
                RemoveL0CacheForNpc(npcId);
                _l1BlockCache.Remove(npcId);
                _l1Version.Remove(npcId);
                _l1KeyVersions.Remove(npcId);
                _diffStore.Remove(npcId);
                _keyLastValues.Remove(npcId);
                _historyManager.ClearHistory(npcId);
                _embedCache.InvalidateNpc(npcId);
                SemanticEmbedding.InvalidateNpc(npcId);
            }
        }

        private ChatMessage? BuildL0(string npcId, string scenario, List<KeyMeta> keys, Pawn? pawn)
        {
            string cacheKey = $"{npcId}_{scenario}";
            if (_l0Cache.TryGetValue(cacheKey, out var cached))
            {
                foreach (var key in keys)
                    _pendingCacheEvents[$"L0_{key.Key}"] = true;
                return cached;
            }

            foreach (var key in keys)
                _pendingCacheEvents[$"L0_{key.Key}"] = false;

            var sb = new StringBuilder();

            foreach (var key in keys)
            {
                var entries = key.ValueProvider(pawn!);
                if (entries == null) continue;
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.Content))
                        sb.AppendLine(entry.Content);
                }
            }

            string content = sb.ToString().TrimEnd();
            if (string.IsNullOrEmpty(content)) return null;

            var msg = new ChatMessage { Role = "system", Content = content };
            _l0Cache[cacheKey] = msg;
            return msg;
        }

        private ChatMessage? BuildL1(string npcId, List<KeyMeta> keys, Pawn? pawn)
        {
            if (keys.Count == 0) return null;

            bool changed = false;
            if (_l1KeyVersions.TryGetValue(npcId, out var versions))
            {
                foreach (var key in keys)
                {
                    if (pawn == null) continue;
                    string currentVal = EntriesToString(key.ValueProvider(pawn));
                    if (!versions.TryGetValue(key.Key, out var ver))
                    {
                        changed = true;
                        break;
                    }
                    if (_keyLastValues.TryGetValue(npcId, out var lastVals) &&
                        lastVals.TryGetValue(key.Key, out var lastVal) &&
                        lastVal != currentVal)
                    {
                        changed = true;
                        AddDiff(npcId, key.Key, lastVal, currentVal, key.Layer);
                    }
                }
            }
            else
            {
                changed = true;
            }

            if (!changed && _l1BlockCache.TryGetValue(npcId, out var existingBlocks))
            {
                foreach (var key in keys)
                    _pendingCacheEvents[$"L1_{key.Key}"] = true;
                return AssembleL1Message(existingBlocks);
            }

            foreach (var key in keys)
                _pendingCacheEvents[$"L1_{key.Key}"] = false;

            if (!_l1BlockCache.TryGetValue(npcId, out var blocks))
            {
                blocks = new Dictionary<string, string>();
                _l1BlockCache[npcId] = blocks;
            }

            foreach (var key in keys)
            {
                if (pawn == null) continue;
                var entries = key.ValueProvider(pawn);
                string val = EntriesToString(entries);
                if (!string.IsNullOrEmpty(val))
                    blocks[key.Key] = val;
                else
                    blocks.Remove(key.Key);
            }

            int newVersion = (_l1Version.TryGetValue(npcId, out var oldVer) ? oldVer : 0) + 1;
            _l1Version[npcId] = newVersion;

            if (!_l1KeyVersions.ContainsKey(npcId))
                _l1KeyVersions[npcId] = new Dictionary<string, int>();
            foreach (var key in keys)
                _l1KeyVersions[npcId][key.Key] = newVersion;

            return AssembleL1Message(blocks);
        }

        private ChatMessage? AssembleL1Message(Dictionary<string, string> blocks)
        {
            if (blocks.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var kvp in blocks)
            {
                sb.AppendLine($"[{kvp.Key}]");
                sb.AppendLine(kvp.Value);
            }
            string content = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(content) ? null : new ChatMessage { Role = "system", Content = content };
        }

        private ChatMessage? BuildL2L3(List<KeyMeta> keys, Pawn? pawn)
        {
            if (keys.Count == 0 || pawn == null) return null;

            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                var entries = key.ValueProvider(pawn);
                if (entries == null) continue;
                var sbEntries = new StringBuilder();
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.Content))
                        sbEntries.AppendLine(entry.Content);
                }
                string val = sbEntries.ToString().TrimEnd();
                if (!string.IsNullOrEmpty(val))
                {
                    sb.AppendLine($"[{key.Key}]");
                    sb.AppendLine(val);
                }
            }

            string content = sb.ToString().TrimEnd();
            if (string.IsNullOrEmpty(content)) return null;

            return new ChatMessage { Role = "system", Content = content };
        }

        private ChatMessage? BuildL5(List<KeyMeta> keys, Pawn? pawn)
        {
            if (keys.Count == 0 || pawn == null) return null;
            var sb = new StringBuilder();
            sb.AppendLine("Perception:");
            foreach (var key in keys)
            {
                var entries = key.ValueProvider(pawn);
                if (entries == null) continue;
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.Content))
                        sb.AppendLine($"  {entry.Content}");
                }
            }
            string content = sb.ToString().TrimEnd();
            if (string.IsNullOrEmpty(content)) return null;
            return new ChatMessage { Role = "system", Content = content };
        }

        private ChatMessage? BuildDiffMessage(string npcId, ContextLayer layer)
        {
            if (!_diffStore.TryGetValue(npcId, out var diffs) || diffs.Count == 0)
                return null;

            var layerDiffs = diffs.Where(d => d.Layer == layer).ToList();
            if (layerDiffs.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("RimMind.Core.Prompt.EnvironmentChange".Translate());
            foreach (var diff in layerDiffs)
                sb.AppendLine(diff.Format());

            return new ChatMessage { Role = "system", Content = sb.ToString().TrimEnd() };
        }

        private void AddDiff(string npcId, string key, string oldValue, string newValue, ContextLayer layer)
        {
            if (!_diffStore.ContainsKey(npcId))
                _diffStore[npcId] = new List<ContextDiff>();

            var existing = _diffStore[npcId].FirstOrDefault(d => d.Key == key);
            if (existing != null)
            {
                existing.NewValue = newValue;
                existing.Layer = layer;
                existing.ExpireTick = (Find.TickManager?.TicksGame ?? 0) + ContextDiff.DefaultLifetimeTicks;
            }
            else
            {
                _diffStore[npcId].Add(new ContextDiff
                {
                    Key = key,
                    Layer = layer,
                    OldValue = oldValue,
                    NewValue = newValue,
                    InsertedTick = Find.TickManager?.TicksGame ?? 0,
                    ExpireTick = (Find.TickManager?.TicksGame ?? 0) + ContextDiff.DefaultLifetimeTicks,
                });
            }
        }

        private void MergeExpiredDiffs(string npcId)
        {
            if (!_diffStore.TryGetValue(npcId, out var diffs)) return;

            var expired = diffs.Where(d => d.IsExpired(Find.TickManager?.TicksGame ?? 0)).ToList();
            if (expired.Count > 0)
            {
                if (_l1BlockCache.TryGetValue(npcId, out var blocks))
                {
                    foreach (var diff in expired)
                    {
                        if (diff.Layer == ContextLayer.L1_Baseline && blocks.TryGetValue(diff.Key, out var blockContent))
                        {
                            if (!string.IsNullOrEmpty(diff.OldValue) && blockContent.Contains(diff.OldValue))
                                blocks[diff.Key] = blockContent.Replace(diff.OldValue, diff.NewValue);
                            else
                                blocks[diff.Key] = diff.NewValue;
                        }
                        diffs.Remove(diff);
                    }
                }
                else
                {
                    foreach (var diff in expired)
                        diffs.Remove(diff);
                }

                int newVersion = (_l1Version.TryGetValue(npcId, out var v) ? v : 0) + 1;
                _l1Version[npcId] = newVersion;

                if (_l1KeyVersions.TryGetValue(npcId, out var versions))
                    foreach (var diff in expired.Where(d => d.Layer == ContextLayer.L1_Baseline))
                        versions[diff.Key] = newVersion;
            }

        }

        private void UpdateKeyValues(string npcId, List<KeyMeta> keys, Pawn? pawn)
        {
            if (pawn == null) return;
            if (!_keyLastValues.ContainsKey(npcId))
                _keyLastValues[npcId] = new Dictionary<string, string>();

            foreach (var key in keys)
            {
                var entries = key.ValueProvider(pawn);
                string val = EntriesToString(entries);
                if (_keyLastValues[npcId].TryGetValue(key.Key, out var lastVal))
                {
                    if (lastVal != val)
                    {
                        _scheduler.OnKeyUpdated(key);
                        if (key.Layer != ContextLayer.L1_Baseline)
                        {
                            AddDiff(npcId, key.Key, lastVal, val, key.Layer);
                        }
                        _embedCache.InvalidateBlock(npcId, key.Key);
                        _embedCache.InvalidateEntries(npcId, key.Key);
                        SemanticEmbedding.InvalidateBlockEmbedding(npcId, key.Key);
                        SemanticEmbedding.InvalidateEntryEmbeddings(npcId, key.Key);
                    }
                }
                _keyLastValues[npcId][key.Key] = val;
            }
        }

        private static string EntriesToString(List<ContextEntry>? entries)
        {
            if (entries == null || entries.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Content))
                    sb.AppendLine(entry.Content);
            }
            return sb.ToString().TrimEnd();
        }

        private void ApplyBudgetTrim(ContextSnapshot snapshot)
        {
            if (snapshot.Messages == null || snapshot.Messages.Count == 0) return;

            int totalBudget = FlywheelParameterStore.Instance?.TotalBudget ?? 4000;
            int reserveForOutput = RimMindCoreMod.Settings?.maxTokens > 0
                ? RimMindCoreMod.Settings.maxTokens
                : 800;
            float budgetRatio = RimMindCoreMod.Settings?.Context?.ContextBudget ?? 0.6f;
            int available = (int)(totalBudget * budgetRatio) - reserveForOutput;
            if (available <= 0) available = totalBudget - reserveForOutput;

            if (snapshot.EstimatedTokens <= available) return;

            // Messages -> PromptSections with priority based on role + LayerTag
            var sections = new List<PromptSection>();
            foreach (var msg in snapshot.Messages)
            {
                int priority = msg.Role switch
                {
                    "system" when msg.LayerTag == "L0" => PromptSection.PriorityCore,
                    "system" => PromptSection.PriorityKeyState,
                    "user" => PromptSection.PriorityCurrentInput,
                    "assistant" => PromptSection.PriorityAuxiliary,
                    _ => PromptSection.PriorityAuxiliary
                };

                var section = new PromptSection(msg.Role ?? "unknown", msg.Content ?? "", priority)
                {
                    LayerTag = msg.LayerTag
                };

                // L2/L3 system messages: add compress callback for detail->brief
                if (msg.Role == "system" && (msg.LayerTag == "L2" || msg.LayerTag == "L3" || msg.LayerTag == "L5"))
                {
                    section.Compress = CompressToBrief;
                }

                sections.Add(section);
            }

            // Compose via PromptBudget
            var budget = new PromptBudget(totalBudget, reserveForOutput);
            var trimmed = budget.Compose(sections);

            // Write back to snapshot
            snapshot.Messages.Clear();
            foreach (var sec in trimmed)
            {
                snapshot.Messages.Add(new ChatMessage
                {
                    Role = sec.Tag,
                    Content = sec.Content,
                    LayerTag = sec.LayerTag
                });
            }
            snapshot.EstimatedTokens = trimmed.Sum(s => s.EstimatedTokens);
            snapshot.Meta.TotalTokens = snapshot.EstimatedTokens;

            if (RimMindCoreMod.Settings?.debugLogging == true)
            {
                Log.Message($"[RimMind] Budget trim applied for {snapshot.NpcId}: trimmed to {snapshot.EstimatedTokens} tokens (budget: {available})");
            }
        }

        private static string CompressToBrief(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            const int briefLimit = 200;
            if (content.Length <= briefLimit) return content;
            int cut = briefLimit;
            if (char.IsHighSurrogate(content[cut - 1])) cut--;
            return content.Substring(0, cut) + "...";
        }

        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int cjk = 0, other = 0;
            foreach (char c in text)
            {
                if (c > 0x2E80) cjk++;
                else other++;
            }
            return (int)(other / 4.0 + cjk / 1.5 + 0.5);
        }
    }
}
