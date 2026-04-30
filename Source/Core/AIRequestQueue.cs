using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Core.AgentBus;
using RimMind.Core.Client;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Internal
{
    public class AIRequestQueue : GameComponent
    {
        private readonly ConcurrentQueue<(AIResponse response, Action<AIResponse> callback)> _results
            = new ConcurrentQueue<(AIResponse, Action<AIResponse>)>();

        private readonly ConcurrentQueue<(string msg, bool isWarning)> _pendingLogs
            = new ConcurrentQueue<(string, bool)>();

        private enum FireResultKind { Complete, Retry }

        private struct PendingFireResult
        {
            public FireResultKind Kind;
            public TrackedRequest Tracked;
            public AIResponse Response;
        }

        private readonly ConcurrentQueue<PendingFireResult> _pendingFireResults
            = new ConcurrentQueue<PendingFireResult>();

        private readonly object _dictLock = new object();
        private readonly Dictionary<string, int> _modCooldowns = new Dictionary<string, int>();
        private readonly Dictionary<string, List<TrackedRequest>> _modQueues
            = new Dictionary<string, List<TrackedRequest>>();
        private readonly Dictionary<int, TrackedRequest> _activeRequests
            = new Dictionary<int, TrackedRequest>();
        private readonly Dictionary<string, TrackedRequest> _requestIdToActive
            = new Dictionary<string, TrackedRequest>();

        private int _lastQueueProcessTick;
        private int _nextTrackingId;
        private bool _isPaused;
        private bool _isProcessingLocalRequest;

        private const int QueueProcessInterval = 60;

        private static AIRequestQueue? _instance;
        public static AIRequestQueue Instance => _instance ?? throw new InvalidOperationException("AIRequestQueue has not been initialized by GameComponent system.");

        public static void LogFromBackground(string msg, bool isWarning = false)
            => _instance?._pendingLogs.Enqueue((msg, isWarning));

        public AIRequestQueue(Game game)
        {
            _instance = this;
        }

        public override void StartedNewGame()
        {
            foreach (var kvp in _activeRequests)
            {
                var response = AIResponse.Cancelled(kvp.Value.Request.RequestId, "New game started, request cancelled");
                response.Priority = kvp.Value.Request.Priority;
                _results.Enqueue((response, kvp.Value.Callback));
            }
            _modCooldowns.Clear();
            ClearAllQueues();
            _activeRequests.Clear();
            _requestIdToActive.Clear();
            _isProcessingLocalRequest = false;
            _isPaused = false;
        }

        public override void LoadedGame()
        {
            foreach (var kvp in _activeRequests)
            {
                var response = AIResponse.Cancelled(kvp.Value.Request.RequestId, "Game loaded, request cancelled");
                response.Priority = kvp.Value.Request.Priority;
                _results.Enqueue((response, kvp.Value.Callback));
            }
            _modCooldowns.Clear();
            ClearAllQueues();
            _activeRequests.Clear();
            _requestIdToActive.Clear();
            _isProcessingLocalRequest = false;
            _isPaused = false;
        }

        public override void GameComponentTick()
        {
            global::RimMind.Core.AgentBus.AgentBus.FlushBackgroundQueue();

            while (_pendingLogs.TryDequeue(out var log))
            {
                if (log.isWarning) Log.Warning(log.msg);
                else Log.Message(log.msg);
            }

            while (_pendingFireResults.TryDequeue(out var fireResult))
            {
                ProcessFireResult(fireResult);
            }

            while (_results.TryDequeue(out var item))
            {
                try { item.callback?.Invoke(item.response); }
                catch (Exception ex)
                {
                    Log.Error($"[RimMind] Callback exception for {item.response.RequestId}: {ex}");
                }
            }

            CheckActiveRequestTimeouts();

            int now = Find.TickManager.TicksGame;
            if (now - _lastQueueProcessTick >= QueueProcessInterval)
            {
                _lastQueueProcessTick = now;
                ProcessAllQueues(now);
            }
        }

        public void Enqueue(AIRequest request, Action<AIResponse> callback, IAIClient client)
        {
            lock (_dictLock)
            {
                string modId = !string.IsNullOrEmpty(request.ModId) ? request.ModId : "Unknown";
                var settings = RimMindCoreMod.Settings;

                if (!_modQueues.TryGetValue(modId, out var queue))
                {
                    queue = new List<TrackedRequest>();
                    _modQueues[modId] = queue;
                }

                int trackingId = _nextTrackingId++;
                var tracked = new TrackedRequest
                {
                    TrackingId = trackingId,
                    Request = request,
                    Callback = callback,
                    Client = client,
                    IsLocalEndpointSnapshot = client.IsLocalEndpoint,
                    State = AIRequestState.Queued,
                    EnqueuedAtTick = Find.TickManager.TicksGame,
                    EnqueuedAtMs = Stopwatch.GetTimestamp(),
                    AttemptCount = 1,
                    MaxAttempts = request.MaxRetryCount >= 0 ? request.MaxRetryCount + 1 : RimMindCoreMod.Settings.maxRetryCount + 1,
                };

                int insertIdx = queue.FindIndex(t => t.Request.Priority > request.Priority);
                if (insertIdx >= 0)
                    queue.Insert(insertIdx, tracked);
                else
                    queue.Add(tracked);

                if (settings.debugLogging)
                    Log.Message($"[RimMind][Core] Enqueued request {request.RequestId} (track={trackingId}) for mod {modId}, " +
                                $"priority={request.Priority}, queue depth={queue.Count}");

                int now = Find.TickManager.TicksGame;
                TryProcessModQueue(modId, now);
            }
        }

        public void EnqueueImmediate(AIRequest request, Action<AIResponse> callback, IAIClient client)
        {
            lock (_dictLock)
            {
                var settings = RimMindCoreMod.Settings;

                if (client.IsLocalEndpoint && _isProcessingLocalRequest)
                {
                    if (settings.debugLogging)
                        Log.Message($"[RimMind][Core] Immediate request {request.RequestId} deferred: local model busy");

                    Enqueue(request, callback, client);
                    return;
                }

                int trackingId = _nextTrackingId++;
                var tracked = new TrackedRequest
                {
                    TrackingId = trackingId,
                    Request = request,
                    Callback = callback,
                    Client = client,
                    IsLocalEndpointSnapshot = client.IsLocalEndpoint,
                    State = AIRequestState.Processing,
                    EnqueuedAtTick = Find.TickManager.TicksGame,
                    EnqueuedAtMs = Stopwatch.GetTimestamp(),
                    StartedProcessingAtTick = Find.TickManager.TicksGame,
                    AttemptCount = 1,
                    MaxAttempts = 1,
                };

                _activeRequests[trackingId] = tracked;
                _requestIdToActive[request.RequestId] = tracked;

                if (client.IsLocalEndpoint)
                    _isProcessingLocalRequest = true;

                if (settings.debugLogging)
                    Log.Message($"[RimMind][Core] Immediate request {request.RequestId} (track={trackingId}) for mod {request.ModId}, bypassing queue");

                FireRequest(tracked);
            }
        }

        private void ProcessAllQueues(int now)
        {
            if (_isPaused) return;

            lock (_dictLock)
            {
                var readyRequests = new List<(string modId, TrackedRequest tracked)>();

                foreach (var kvp in _modQueues)
                {
                    string modId = kvp.Key;
                    var queue = kvp.Value;

                    if (queue.Count == 0) continue;
                    if (_modCooldowns.TryGetValue(modId, out int nextAllowed) && now < nextAllowed)
                        continue;

                    while (queue.Count > 0)
                    {
                        var tracked = queue[0];
                        if (tracked.Request.ExpireAtTicks > 0 && now > tracked.Request.ExpireAtTicks)
                        {
                            queue.RemoveAt(0);
                            if (RimMindCoreMod.Settings.debugLogging)
                                Log.Message($"[RimMind][Core] Expired request {tracked.Request.RequestId} skipped (expired at {tracked.Request.ExpireAtTicks}, now={now})");
                            continue;
                        }
                        break;
                    }

                    if (queue.Count > 0)
                        readyRequests.Add((modId, queue[0]));
                }

                readyRequests.Sort((a, b) =>
                {
                    int p = (int)a.tracked.Request.Priority - (int)b.tracked.Request.Priority;
                    if (p != 0) return p;
                    return a.tracked.EnqueuedAtTick - b.tracked.EnqueuedAtTick;
                });

                int maxConcurrent = RimMindCoreMod.Settings.maxConcurrentRequests;

                foreach (var (modId, tracked) in readyRequests)
                {
                    if (_activeRequests.Count >= maxConcurrent)
                        break;

                    if (tracked.IsLocalEndpointSnapshot && _isProcessingLocalRequest)
                        continue;

                    if (!_modQueues.TryGetValue(modId, out var queue) || queue.Count == 0 || queue[0] != tracked)
                        continue;

                    queue.RemoveAt(0);

                    int cooldownTicks = GetModCooldownTicks(modId);
                    _modCooldowns[modId] = now + cooldownTicks;

                    tracked.State = AIRequestState.Processing;
                    tracked.StartedProcessingAtTick = now;
                    _activeRequests[tracked.TrackingId] = tracked;
                    _requestIdToActive[tracked.Request.RequestId] = tracked;

                    if (tracked.IsLocalEndpointSnapshot)
                        _isProcessingLocalRequest = true;

                    if (RimMindCoreMod.Settings.debugLogging)
                        Log.Message($"[RimMind][Core] Processing request {tracked.Request.RequestId} (track={tracked.TrackingId}) " +
                                    $"for mod {modId}, priority={tracked.Request.Priority}, cooldown={cooldownTicks}t, " +
                                    $"active={_activeRequests.Count}/{maxConcurrent}");

                    FireRequest(tracked);
                }
            }
        }

        private void TryProcessModQueue(string modId, int now)
        {
            if (_isPaused) return;
            if (_modCooldowns.TryGetValue(modId, out int nextAllowed) && now < nextAllowed)
                return;
            if (!_modQueues.TryGetValue(modId, out var queue) || queue.Count == 0)
                return;

            ProcessAllQueues(now);
        }

        private void FireRequest(TrackedRequest tracked)
        {
            Task.Run(async () =>
            {
                AIResponse response;
                try
                {
                    response = await tracked.Client.SendAsync(tracked.Request);
                }
                catch (Exception ex)
                {
                    AIRequestQueue.LogFromBackground(
                        $"[RimMind] SendAsync threw for {tracked.Request.RequestId}: {ex.Message}", isWarning: true);
                    response = AIResponse.Failure(tracked.Request.RequestId, ex.Message);
                }

                long queueWaitMs = 0;
                if (tracked.StartedProcessingAtTick > 0 && tracked.EnqueuedAtTick > 0)
                {
                    queueWaitMs = (tracked.StartedProcessingAtTick - tracked.EnqueuedAtTick) * 16L;
                }

                response.AttemptCount = tracked.AttemptCount;
                response.QueueWaitMs = queueWaitMs;
                response.Priority = tracked.Request.Priority;

                bool shouldRetry = !response.Success
                    && tracked.AttemptCount < tracked.MaxAttempts
                    && IsTransientError(response.Error);

                Instance._pendingFireResults.Enqueue(new PendingFireResult
                {
                    Kind = shouldRetry ? FireResultKind.Retry : FireResultKind.Complete,
                    Tracked = tracked,
                    Response = response,
                });
            });
        }

        private void ProcessFireResult(PendingFireResult result)
        {
            lock (_dictLock)
            {
                var tracked = result.Tracked;

                _activeRequests.Remove(tracked.TrackingId);
                _requestIdToActive.Remove(tracked.Request.RequestId);

                if (tracked.IsLocalEndpointSnapshot)
                    _isProcessingLocalRequest = false;

                if (result.Kind == FireResultKind.Retry)
                {
                    tracked.AttemptCount++;
                    tracked.State = AIRequestState.Queued;
                    tracked.StartedProcessingAtTick = 0;

                    string modId = tracked.Request.ModId;
                    if (!_modQueues.TryGetValue(modId, out var queue))
                    {
                        queue = new List<TrackedRequest>();
                        _modQueues[modId] = queue;
                    }

                    int insertIdx = queue.FindIndex(t => t.Request.Priority > tracked.Request.Priority);
                    if (insertIdx >= 0)
                        queue.Insert(insertIdx, tracked);
                    else
                        queue.Add(tracked);

                    if (RimMindCoreMod.Settings.debugLogging)
                        Log.Message($"[RimMind] Retrying request {tracked.Request.RequestId} (attempt {tracked.AttemptCount}/{tracked.MaxAttempts})");
                }
                else
                {
                    if (!result.Response.Success && QuotaExceededException.IsQuotaError(result.Response.Error))
                    {
                        Log.Warning($"[RimMind] Player2 quota exceeded for request {tracked.Request.RequestId}. " +
                                    "Please top up your Joules balance or switch to another provider.");
                    }
                    _results.Enqueue((result.Response, tracked.Callback));
                }
            }
        }

        private void CheckActiveRequestTimeouts()
        {
            lock (_dictLock)
            {
                if (_activeRequests.Count == 0) return;

                int now = Find.TickManager.TicksGame;
                int timeoutMs = RimMindCoreMod.Settings.requestTimeoutMs;
                int timeoutTicks = timeoutMs / 16;

                var timedOut = new List<TrackedRequest>();

                foreach (var kvp in _activeRequests)
                {
                    var tracked = kvp.Value;
                    if (tracked.StartedProcessingAtTick > 0 && now - tracked.StartedProcessingAtTick > timeoutTicks)
                    {
                        timedOut.Add(tracked);
                    }
                }

                foreach (var tracked in timedOut)
                {
                    _activeRequests.Remove(tracked.TrackingId);
                    _requestIdToActive.Remove(tracked.Request.RequestId);

                    if (tracked.IsLocalEndpointSnapshot)
                        _isProcessingLocalRequest = false;

                    var response = AIResponse.Failure(tracked.Request.RequestId,
                        $"Request timed out after {timeoutMs}ms");
                    response.AttemptCount = tracked.AttemptCount;
                    response.Priority = tracked.Request.Priority;
                    response.State = AIRequestState.Error;

                    _results.Enqueue((response, tracked.Callback));

                    if (RimMindCoreMod.Settings.debugLogging)
                        Log.Message($"[RimMind][Core] Request {tracked.Request.RequestId} timed out after {timeoutTicks} ticks");
                }
            }
        }

        private static bool IsTransientError(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            if (QuotaExceededException.IsQuotaError(error)) return false;
            string lower = error.ToLowerInvariant();
            return lower.Contains("timeout")
                || lower.Contains("connection")
                || lower.Contains("network")
                || lower.Contains("503")
                || lower.Contains("502")
                || lower.Contains("429")
                || lower.Contains("rate limit");
        }

        public bool CancelRequest(string requestId)
        {
            lock (_dictLock)
            {
                if (_requestIdToActive.TryGetValue(requestId, out var active))
                {
                    active.State = AIRequestState.Cancelled;
                    _activeRequests.Remove(active.TrackingId);
                    _requestIdToActive.Remove(requestId);

                    if (active.IsLocalEndpointSnapshot)
                        _isProcessingLocalRequest = false;

                    var response = AIResponse.Cancelled(requestId, "Cancelled by user");
                    response.Priority = active.Request.Priority;
                    _results.Enqueue((response, active.Callback));
                    return true;
                }

                foreach (var kvp in _modQueues)
                {
                    var queue = kvp.Value;
                    int idx = queue.FindIndex(t => t.Request.RequestId == requestId);
                    if (idx >= 0)
                    {
                        var tracked = queue[idx];
                        queue.RemoveAt(idx);
                        var response = AIResponse.Cancelled(requestId, "Cancelled by user");
                        response.Priority = tracked.Request.Priority;
                        _results.Enqueue((response, tracked.Callback));
                        return true;
                    }
                }

                return false;
            }
        }

        public void PauseQueue() => _isPaused = true;

        public void ResumeQueue() => _isPaused = false;

        public bool IsPaused => _isPaused;

        public int ActiveRequestCount => _activeRequests.Count;

        public bool IsLocalModelBusy => _isProcessingLocalRequest;

        public IReadOnlyList<TrackedRequest> GetActiveRequests()
            => _activeRequests.Values.ToList();

        private int GetModCooldownTicks(string modId)
        {
            var getter = RimMindAPI.GetModCooldownGetter(modId);
            if (getter != null)
            {
                try { return getter(); }
                catch (Exception ex) { Log.Warning($"[RimMind] GetModCooldown failed: {ex.Message}"); }
            }
            return 3600;
        }

        public int GetCooldownTicksLeft(string modId)
        {
            if (!_modCooldowns.TryGetValue(modId, out int nextAllowed)) return 0;
            int left = nextAllowed - Find.TickManager.TicksGame;
            return left > 0 ? left : 0;
        }

        public int GetQueueDepth(string modId)
        {
            if (!_modQueues.TryGetValue(modId, out var queue)) return 0;
            return queue.Count;
        }

        public void ClearCooldown(string modId) => _modCooldowns.Remove(modId);

        public void ClearAllCooldowns() => _modCooldowns.Clear();

        public void ClearAllQueues()
        {
            foreach (var kvp in _modQueues)
                kvp.Value.Clear();
            _modQueues.Clear();
        }

        public IReadOnlyDictionary<string, int> GetAllCooldowns() => _modCooldowns;

        public IReadOnlyDictionary<string, int> GetAllQueueDepths()
        {
            var result = new Dictionary<string, int>();
            foreach (var kvp in _modQueues)
                result[kvp.Key] = kvp.Value.Count;
            return result;
        }

        public IReadOnlyList<TrackedRequest> GetQueuedRequests(string modId)
        {
            if (!_modQueues.TryGetValue(modId, out var queue)) return new List<TrackedRequest>();
            return queue.ToList();
        }

        public IReadOnlyList<TrackedRequest> GetAllQueuedRequests()
        {
            var result = new List<TrackedRequest>();
            foreach (var kvp in _modQueues)
                result.AddRange(kvp.Value);
            return result;
        }

        public int TotalQueuedCount => _modQueues.Values.Sum(q => q.Count);

        public class TrackedRequest
        {
            public int TrackingId;
            public AIRequest Request = null!;
            public Action<AIResponse> Callback = null!;
            public IAIClient Client = null!;
            public bool IsLocalEndpointSnapshot;
            public AIRequestState State;
            public int EnqueuedAtTick;
            public long EnqueuedAtMs;
            public int StartedProcessingAtTick;
            public int AttemptCount;
            public int MaxAttempts;
        }
    }
}
