using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using AIRequestState = RimMind.Domain.Llm.AIRequestState;

namespace RimMind.Application.Features.Requests.Queue
{
    public class RequestQueue : ITickableRequestQueue
    {
        private const long TicksPerMillisecond = RimMindDefaults.TicksPerMillisecond;

        private readonly RequestCompletionInbox _completionInbox = new RequestCompletionInbox();

        private readonly object _queueLock = new object();
        private readonly ConcurrentDictionary<string, List<TrackedRequest>> _modQueues
            = new ConcurrentDictionary<string, List<TrackedRequest>>();
        private readonly ConcurrentDictionary<int, TrackedRequest> _activeRequests
            = new ConcurrentDictionary<int, TrackedRequest>();
        private readonly ConcurrentDictionary<string, TrackedRequest> _requestIdToActive
            = new ConcurrentDictionary<string, TrackedRequest>();

        private readonly QueueCircuitBreaker _circuitBreaker;

        private readonly ILogSink? _logSink;
        private readonly ICompletionFence _completionFence;

        private ILogSink? LogSink => _logSink;

        private readonly Func<ISettingsProvider?>? _settingsFactory;

        private int _lastQueueProcessTick;
        private int _nextTrackingId;
        private bool _isPaused;
        private bool _isProcessingLocalRequest;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public int CurrentTick { get; set; }
        public Action<string, bool>? LogHandler { get; set; }
        internal int PendingCallbackCount => _completionInbox.PendingCallbackCount;

        private ISettingsProvider Settings => _settingsFactory?.Invoke() ?? new DefaultSettingsProvider();

        private int QueueProcessInterval => Settings.QueueProcessInterval;

        public RequestQueue(
            Func<ISettingsProvider?>? settingsFactory = null,
            ILogSink? logSink = null,
            ICompletionFence? completionFence = null)
        {
            _settingsFactory = settingsFactory;
            _logSink = logSink;
            _completionFence = completionFence ?? UnboundedCompletionFence.Instance;
            _circuitBreaker = new QueueCircuitBreaker(Settings, logSink);
        }

        public void Tick()
        {
            _completionInbox.Drain(LogHandler);
            CheckActiveRequestTimeouts();
            int now = CurrentTick;
            if (now - _lastQueueProcessTick >= QueueProcessInterval) { _lastQueueProcessTick = now; ProcessAllQueues(now); }
        }

        public void Reset()
        {
            CancelAllRequests();
            lock (_queueLock)
            {
                ClearAllQueues();
                _activeRequests.Clear();
                _requestIdToActive.Clear();
                _isProcessingLocalRequest = false;
                _isPaused = false;
            }
            _completionInbox.Clear();
            _circuitBreaker.ClearAllCooldowns();
        }

        public void CancelAllRequests()
        {
            lock (_queueLock)
            {
                var previous = _cts;
                _cts = new CancellationTokenSource();
                previous.Cancel();

                var cancelled = Result<LlmResponse, RimMindError>.Err(
                    new RimMindError(RimMindErrorCode.Cancelled, "Request cancelled"));
                foreach (var tracked in _activeRequests.Values.ToList())
                {
                    tracked.CancellationSource?.Cancel();
                    Complete(tracked, cancelled);
                }
                foreach (var queue in _modQueues.Values)
                {
                    foreach (var tracked in queue.ToList()) Complete(tracked, cancelled);
                    queue.Clear();
                }
                previous.Dispose();
            }
        }

        public void Enqueue(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>> callback, IAIClient client)
            => Enqueue(
                envelope,
                callback,
                ct => client.SendAsync(CloneWithCancellationToken(envelope, ct)),
                client.IsLocalEndpoint);

        public void Enqueue(
            LlmRequestEnvelope envelope,
            Action<Result<LlmResponse, RimMindError>> callback,
            Func<CancellationToken, Task<Result<LlmResponse, RimMindError>>> executor,
            bool isLocalEndpoint = false)
        {
            lock (_queueLock)
            {
                string modId = !string.IsNullOrEmpty(envelope.ModId) ? envelope.ModId : "Unknown";
                if (!_modQueues.TryGetValue(modId, out var queue)) { queue = new List<TrackedRequest>(); _modQueues[modId] = queue; }
                int trackingId = _nextTrackingId++;
                var tracked = new TrackedRequest
                {
                    TrackingId = trackingId, Envelope = envelope, Callback = callback, Executor = executor,
                    IsLocalEndpointSnapshot = isLocalEndpoint, State = AIRequestState.Queued,
                    EnqueuedAtTick = CurrentTick, AttemptCount = 1, MaxAttempts = 1,
                };
                int insertIdx = queue.FindIndex(t => t.Envelope.Priority > envelope.Priority);
                if (insertIdx >= 0) queue.Insert(insertIdx, tracked); else queue.Add(tracked);
                if (Settings.DebugLogging)
                    EnqueueLog($"[RimMind-Core] Enqueued request {envelope.RequestId} (track={trackingId}) for mod {modId}, priority={envelope.Priority}, queue depth={queue.Count}");
                TryProcessModQueue(modId, CurrentTick);
            }
        }

        public void EnqueueImmediate(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>> callback, IAIClient client)
        {
            lock (_queueLock)
            {
                if (client.IsLocalEndpoint && _isProcessingLocalRequest)
                {
                    if (Settings.DebugLogging) EnqueueLog($"[RimMind-Core] Immediate request {envelope.RequestId} deferred: local model busy");
                    Enqueue(envelope, callback, client);
                    return;
                }
                int trackingId = _nextTrackingId++;
                var tracked = new TrackedRequest
                {
                    TrackingId = trackingId, Envelope = envelope, Callback = callback,
                    Executor = ct => client.SendAsync(CloneWithCancellationToken(envelope, ct)), Client = client,
                    IsLocalEndpointSnapshot = client.IsLocalEndpoint, State = AIRequestState.Processing,
                    EnqueuedAtTick = CurrentTick, StartedProcessingAtTick = CurrentTick, AttemptCount = 1, MaxAttempts = 1,
                };
                _activeRequests[trackingId] = tracked;
                _requestIdToActive[envelope.RequestId] = tracked;
                if (client.IsLocalEndpoint) _isProcessingLocalRequest = true;
                if (Settings.DebugLogging)
                    EnqueueLog($"[RimMind-Core] Immediate request {envelope.RequestId} (track={trackingId}) for mod {envelope.ModId}, bypassing queue");
                FireRequest(tracked);
            }
        }

        private void ProcessAllQueues(int now)
        {
            if (_isPaused) return;
            lock (_queueLock)
            {
                var cooldownSnapshot = _circuitBreaker.GetCooldownSnapshot();
                var readyRequests = new List<(string modId, TrackedRequest tracked)>();
                foreach (var kvp in _modQueues)
                {
                    string modId = kvp.Key; var queue = kvp.Value;
                    if (queue.Count == 0) continue;
                    if (cooldownSnapshot.TryGetValue(modId, out int nextAllowed) && now < nextAllowed) continue;
                    while (queue.Count > 0)
                    {
                        var t = queue[0];
                        if (t.Envelope.ExpireAtTicks.HasValue && t.Envelope.ExpireAtTicks.Value > 0 && now > t.Envelope.ExpireAtTicks.Value)
                        {
                            queue.RemoveAt(0);
                            Complete(t, Result<LlmResponse, RimMindError>.Err(
                                RimMindErrors.Timeout($"Request {t.Envelope.RequestId} expired in queue at tick {now}")));
                            if (Settings.DebugLogging)
                                EnqueueLog($"[RimMind-Core] Expired request {t.Envelope.RequestId} completed with timeout");
                            continue;
                        }
                        break;
                    }
                    if (queue.Count > 0) readyRequests.Add((modId, queue[0]));
                }
                readyRequests.Sort((a, b) => { int p = (int)a.tracked.Envelope.Priority - (int)b.tracked.Envelope.Priority; if (p != 0) return p; return a.tracked.EnqueuedAtTick - b.tracked.EnqueuedAtTick; });
                int maxConcurrent = Settings.MaxConcurrentRequests;
                foreach (var (modId, tracked) in readyRequests)
                {
                    if (_activeRequests.Count >= maxConcurrent) break;
                    if (tracked.IsLocalEndpointSnapshot && _isProcessingLocalRequest) continue;
                    if (!_modQueues.TryGetValue(modId, out var queue) || queue.Count == 0 || queue[0] != tracked) continue;
                    queue.RemoveAt(0);
                    int cooldownTicks = _circuitBreaker.GetModCooldownTicks(modId);
                    _circuitBreaker.SetCooldown(modId, now + cooldownTicks);
                    tracked.State = AIRequestState.Processing; tracked.StartedProcessingAtTick = now;
                    _activeRequests[tracked.TrackingId] = tracked; _requestIdToActive[tracked.Envelope.RequestId] = tracked;
                    if (tracked.IsLocalEndpointSnapshot) _isProcessingLocalRequest = true;
                    if (Settings.DebugLogging)
                        EnqueueLog($"[RimMind-Core] Processing request {tracked.Envelope.RequestId} (track={tracked.TrackingId}) for mod {modId}, priority={tracked.Envelope.Priority}, cooldown={cooldownTicks}t, active={_activeRequests.Count}/{maxConcurrent}");
                    FireRequest(tracked);
                }
            }
        }

        private void TryProcessModQueue(string modId, int now)
        {
            if (_isPaused) return;
            if (_circuitBreaker.IsOnCooldown(modId, now)) return;
            lock (_queueLock) { if (!_modQueues.TryGetValue(modId, out var q) || q.Count == 0) return; }
            ProcessAllQueues(now);
        }

        private void FireRequest(TrackedRequest tracked)
        {
            tracked.CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                _cts.Token,
                tracked.Envelope.Ct,
                _completionFence.CancellationToken);
            var ct = tracked.CancellationSource.Token;
            Task.Run(async () =>
            {
                Result<LlmResponse, RimMindError> result;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    result = await tracked.Executor(ct);
                }
                catch (OperationCanceledException)
                {
                    result = Result<LlmResponse, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.Cancelled, "Request cancelled"));
                }
                catch (Exception ex)
                {
                    LogSink?.LogFromBackground($"[RimMind-Core] Execute threw for {tracked.Envelope.RequestId}: {ex.Message}", isWarning: true);
                    result = Result<LlmResponse, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.InternalError, ex.Message));
                }
                Complete(tracked, result);
                tracked.CancellationSource?.Dispose();
            });
        }

        private void Complete(TrackedRequest tracked, Result<LlmResponse, RimMindError> result)
        {
            if (Interlocked.Exchange(ref tracked.CompletionQueued, 1) != 0) return;
            if (!_completionFence.TryAcceptCompletion()) return;
            lock (_queueLock)
            {
                if (_activeRequests.TryRemove(tracked.TrackingId, out _) && tracked.IsLocalEndpointSnapshot)
                    _isProcessingLocalRequest = false;
                _requestIdToActive.TryRemove(tracked.Envelope.RequestId, out _);
            }
            EnqueueCompletion(tracked, result);
        }

        private void EnqueueCompletion(
            TrackedRequest tracked,
            Result<LlmResponse, RimMindError> result)
        {
            _completionInbox.Enqueue(result, tracked.Callback, _completionFence);
        }

        private void CheckActiveRequestTimeouts()
        {
            lock (_queueLock)
            {
                if (_activeRequests.Count == 0) return;
                int now = CurrentTick; int timeoutMs = Settings.RequestTimeoutMs; int timeoutTicks = timeoutMs / (int)TicksPerMillisecond;
                var timedOut = new List<TrackedRequest>();
                foreach (var kvp in _activeRequests) { if (kvp.Value.StartedProcessingAtTick > 0 && now - kvp.Value.StartedProcessingAtTick > timeoutTicks) timedOut.Add(kvp.Value); }
                foreach (var tracked in timedOut)
                {
                    tracked.CancellationSource?.Cancel();
                    var errResult = Result<LlmResponse, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.Timeout, $"Request {tracked.Envelope.RequestId} timed out after {timeoutTicks} ticks"));
                    Complete(tracked, errResult);
                    if (Settings.DebugLogging) EnqueueLog($"[RimMind-Core] Request {tracked.Envelope.RequestId} timed out after {timeoutTicks} ticks");
                }
            }
        }

        public bool CancelRequest(string requestId)
        {
            lock (_queueLock)
            {
                if (_requestIdToActive.TryGetValue(requestId, out var active))
                {
                    active.State = AIRequestState.Cancelled; active.CancellationSource?.Cancel();
                    var errResult = Result<LlmResponse, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.Cancelled, "Request cancelled"));
                    Complete(active, errResult); return true;
                }
                foreach (var kvp in _modQueues)
                {
                    int idx = kvp.Value.FindIndex(t => t.Envelope.RequestId == requestId);
                    if (idx >= 0)
                    {
                        var tracked = kvp.Value[idx]; kvp.Value.RemoveAt(idx);
                        var errResult = Result<LlmResponse, RimMindError>.Err(
                            new RimMindError(RimMindErrorCode.Cancelled, "Request cancelled"));
                        Complete(tracked, errResult); return true;
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
        public IReadOnlyList<TrackedRequest> GetActiveRequests() { lock (_queueLock) { return _activeRequests.Values.ToList(); } }
        public int GetCooldownTicksLeft(string modId) => _circuitBreaker.GetCooldownTicksLeft(modId, CurrentTick);
        public int GetQueueDepth(string modId) { lock (_queueLock) { return _modQueues.TryGetValue(modId, out var q) ? q.Count : 0; } }
        public void ClearCooldown(string modId) => _circuitBreaker.ClearCooldown(modId);
        public void ClearAllCooldowns() => _circuitBreaker.ClearAllCooldowns();
        public void ClearAllQueues() { lock (_queueLock) { foreach (var kvp in _modQueues) kvp.Value.Clear(); _modQueues.Clear(); } }
        public IReadOnlyDictionary<string, int> GetAllCooldowns() => _circuitBreaker.GetAllCooldowns();
        public IReadOnlyDictionary<string, int> GetAllQueueDepths() { lock (_queueLock) { var r = new Dictionary<string, int>(); foreach (var kvp in _modQueues) r[kvp.Key] = kvp.Value.Count; return r; } }
        public IReadOnlyList<TrackedRequest> GetQueuedRequests(string modId) { lock (_queueLock) { return _modQueues.TryGetValue(modId, out var q) ? q.ToList() : new List<TrackedRequest>(); } }
        public IReadOnlyList<TrackedRequest> GetAllQueuedRequests() { lock (_queueLock) { var r = new List<TrackedRequest>(); foreach (var kvp in _modQueues) r.AddRange(kvp.Value); return r; } }
        public int TotalQueuedCount { get { lock (_queueLock) { return _modQueues.Values.Sum(q => q.Count); } } }
        public void EnqueueLog(string msg, bool isWarning = false) =>
            _completionInbox.EnqueueLog(msg, isWarning);
        public void LogFromBackground(string msg, bool isWarning = false) => EnqueueLog(msg, isWarning);
        internal CancellationTokenSource GetCts() => _cts;

        private static LlmRequestEnvelope CloneWithCancellationToken(
            LlmRequestEnvelope envelope,
            CancellationToken cancellationToken)
        {
            return new LlmRequestEnvelope
            {
                RequestId = envelope.RequestId,
                TraceId = envelope.TraceId,
                ScenarioId = envelope.ScenarioId,
                ModId = envelope.ModId,
                Messages = envelope.Messages,
                SystemAugmentations = envelope.SystemAugmentations,
                JsonSchema = envelope.JsonSchema,
                Tools = envelope.Tools,
                ToolDispatchMode = envelope.ToolDispatchMode,
                Examples = envelope.Examples,
                MaxTokens = envelope.MaxTokens,
                Temperature = envelope.Temperature,
                Priority = envelope.Priority,
                ExpireAtTicks = envelope.ExpireAtTicks,
                MaxRetryCount = envelope.MaxRetryCount,
                IsStreaming = envelope.IsStreaming,
                OnStreamChunk = envelope.OnStreamChunk,
                Ct = cancellationToken,
                NpcId = envelope.NpcId,
                GameStateInfo = envelope.GameStateInfo
            };
        }

        private sealed class UnboundedCompletionFence : ICompletionFence
        {
            public static readonly UnboundedCompletionFence Instance = new UnboundedCompletionFence();

            public CancellationToken CancellationToken => CancellationToken.None;

            public bool TryAcceptCompletion() => true;
        }
    }
}
